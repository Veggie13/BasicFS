using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.IO;
using System.Diagnostics;
using System.Net;

namespace SimpleFTP
{

    public partial class Server
    {
        public class Connection
        {
            private static uint NextID = 0;

            public enum State { Idle, Login, List, Upload, Download }

            public const int BufferSize = 4096;

            private Socket _sock = null;
            private Socket _dataSock = null;
            private Thread _workThread = null;
            private Stream _dataMsg = null;
            private byte[] _buffer = new byte[BufferSize + 1];
            private string _cmdLine = "";
            private List<string> _cmdList = new List<string>();

            private ManualResetEvent _reset = new ManualResetEvent(false);
            private ManualResetEvent _waiter = new ManualResetEvent(false);

            private State _state = State.Login;
            public State ConnectionState
            {
                get { return _state; }
            }

            private Server _server;
            public Server Server
            {
                get { return _server; }
            }

            private string _user = "";
            public string Username
            {
                get { return _user; }
                set { _user = value; }
            }

            private string _cwd = "/";
            public string CurrentDirectory
            {
                get { return _cwd; }
                set { _cwd = value; }
            }

            private int _port = -1;
            public int Port
            {
                get { return _port; }
                set { _port = value; }
            }

            private string _remoteHost = "";
            public string RemoteHost
            {
                get { return _remoteHost; }
                set { _remoteHost = value; }
            }

            private uint _id = NextID++;
            public uint ID
            {
                get { return _id; }
            }

            public Connection(Server server)
            {
                _server = server;
            }

            public void Kill()
            {
                _server.RemoveConnection(ID);

                Socket sock = _sock;
                if (sock != null)
                {
                    lock (sock)
                    {
                        sock.Close();
                        _sock = null;
                    }
                    _reset.Set();
                }
            }

            private bool Connected
            {
                get
                {
                    bool result = false;
                    Socket sock = _sock;
                    if (sock != null)
                        lock (sock)
                        {
                            result = sock.Connected;
                        }
                    return result;
                }
            }

            private void Run(Socket sock)
            {
                if (!_server.Running)
                    return;

                _server._allDone.Set();

                _sock = sock;

                if (Connected)
                    SendResponse("220 " + _server.WelcomeMessage);

                while (Connected)
                {
                    //_reset.Reset();

                    int recv = _sock.Receive(_buffer);
                    if (recv == 0)
                    {
                        Kill();
                        return;
                    }

                    _buffer[recv] = 0;
                    _cmdLine += Encoding.ASCII.GetString(_buffer, 0, recv);
                    GetCommandLine();

                    //_reset.WaitOne();
                }
            }

            private void GetCommandLine()
            {
                string temp = "";
                while (!_cmdLine.Equals(""))
                {
                    int nIndex = _cmdLine.IndexOf("\r\n");
                    if (nIndex != 1)
                    {
                        temp = _cmdLine.Substring(0, nIndex);
                        _cmdLine = _cmdLine.Substring(nIndex + 2);
                        if (!temp.Equals(""))
                        {
                            _cmdList.Add(temp);
                            ProcessCommand();
                        }
                    }
                    else break;
                }
            }

            private void ProcessCommand()
            {
                string cmd = "", args = "";

                string buff = _cmdList[0];
                _cmdList.RemoveAt(0);
                int nIndex = buff.IndexOf(" ");
                if (nIndex == -1)
                {
                    cmd = buff;
                }
                else
                {
                    cmd = buff.Substring(0, nIndex);
                    args = buff.Substring(nIndex + 1);
                }
                cmd = cmd.ToUpper();

                _server.EmitCommandReceived(ID, cmd, args);
                
                State nextState = _state;
                string msg = "";
                _server.Handle(this, cmd, args, out nextState, out msg);
                SendResponse(msg);
                _state = nextState;
                
                StartDataJob();
            }

            private void SendResponse(string response)
            {
                _server.EmitResponseSent(ID, response);

                Socket sock = _sock;
                if (sock != null)
                    sock.Send(Encoding.ASCII.GetBytes(response + "\r\n"));
            }

            public string ResolveRelativePath(string path)
            {
                if (path.Equals(""))
                    path = ".";
                if (path[0] != '/')
                    path = _cwd + "/" + path;
                
                string[] segments = path.Split("/".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                List<string> final = new List<string>();
                foreach (string seg in segments)
                {
                    if (seg.Equals("."))
                        continue;
                    if (seg.Equals(".."))
                    {
                        if (final.Count > 0)
                            final.RemoveAt(final.Count - 1);
                    }
                    else
                        final.Add(seg);
                }

                string result = "/" + string.Join("/", final);
                return result.TrimEnd('/');
            }

            public bool ChangeDirectory(string dir)
            {
                dir = ResolveRelativePath(dir);
                if (!_server.ValidatePath(dir))
                    return false;

                _cwd = dir;
                return true;
            }

            public static void AcceptCallback(IAsyncResult result)
            {
                Server server = (Server)result.AsyncState;
                Socket sock = server._listener.EndAccept(result);
                IPEndPoint endpoint = sock.RemoteEndPoint as IPEndPoint;
                string remoteHost = endpoint.Address.ToString();
                Connection conn = server.GetNewConnection(remoteHost);
                
                conn.Run(sock);
            }

            public bool CreateDataJob(Stream msg)
            {
                _dataSock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _dataSock.Connect(_remoteHost, _port);
                if (!_dataSock.Connected)
                {
                    _dataSock = null;
                    _dataMsg = null;
                    return false;
                }
                _dataMsg = msg;
                _workThread = new Thread(DataThread);
                _waiter.Reset();
                _workThread.Start(this);
                return true;
            }

            public void StartDataJob()
            {
                if (_workThread != null && _workThread.IsAlive)
                    _waiter.Set();
            }

            const int PACKET_SIZE = 4096;
            private void DoDataJob()
            {
                _waiter.WaitOne();

                byte[] buffer = new byte[PACKET_SIZE];
                long remaining = _dataMsg.Length - _dataMsg.Position;
                while (remaining > 0)
                {
                    int size = PACKET_SIZE;
                    if (remaining < PACKET_SIZE)
                    {
                        buffer = new byte[remaining];
                        size = (int)remaining;
                    }
                    int count = _dataMsg.Read(buffer, 0, size);
                    if (count > 0)
                    {
                        int sent = _dataSock.Send(buffer);
                        Debug.Assert(size == sent);
                    }
                    
                    remaining = _dataMsg.Length - _dataMsg.Position;
                }

                _dataSock.Close();
                _dataSock = null;
                _dataMsg.Close();
                _dataMsg = null;
                _workThread = null;

                _state = State.Idle;
                SendResponse("226 Transfer complete.");
            }

            private static void DataThread(object o)
            {
                Connection conn = (Connection)o;
                conn.DoDataJob();
            }
        }
    }

}