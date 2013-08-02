using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using BasicFS;

namespace SimpleFTP
{
    public partial class Server
    {
        #region Private Members
        private static DefaultCommandHandler DefaultHandler = new DefaultCommandHandler();

        private ManualResetEvent _allDone = new ManualResetEvent(false);
        private Socket _listener;
        private Thread _dispatcher;
        #endregion

        #region Properties
        private bool _running = false;
        public bool Running
        {
            get { return _running; }
        }

        private int _port = 21;
        public int Port
        {
            get { return _port; }
            set
            {
                TryChange();
                _port = value;
            }
        }

        private string _welcomeMsg = "Welcome to Pablo's FTP Server";
        public string WelcomeMessage
        {
            get { return _welcomeMsg; }
            set
            {
                TryChange();
                _welcomeMsg = value;
            }
        }

        private string _goodbyeMsg = "Bye";
        public string GoodbyeMessage
        {
            get { return _goodbyeMsg; }
            set
            {
                TryChange();
                _goodbyeMsg = value;
            }
        }

        private int _timeout = 5;
        public int Timeout
        {
            get { return _timeout; }
            set
            {
                TryChange();
                _timeout = value;
            }
        }

        private int _maxUsers = 10;
        public int MaxUsers
        {
            get { return _maxUsers; }
            set
            {
                TryChange();
                _maxUsers = value;
            }
        }

        private IUserManager _mgr = null;
        public IUserManager UserManager
        {
            set { _mgr = value; }
        }

        private IFileSystem _fs = null;
        public IFileSystem FileSystem
        {
            get { return _fs; }
            set { _fs = value; }
        }

        private ICommandHandler _handler = DefaultHandler;
        public ICommandHandler Handler
        {
            set
            {
                if (value == null)
                    _handler = DefaultHandler;
                else
                    _handler = value;
            }
        }

        private Dictionary<uint, Connection> _connections = new Dictionary<uint, Connection>();
        public int ConnectionCount
        {
            get { return _connections.Count; }
        }
        #endregion

        #region Events
        public delegate void CommandNotifier(Server sender, uint connId, string cmd, string args);
        public event CommandNotifier OnCommandReceived;

        public delegate void ResponseNotifier(Server sender, uint connId, string msg);
        public event ResponseNotifier OnResponseSent;

        public delegate void ConnectionNotifier(Server sender, uint connId);
        public event ConnectionNotifier OnConnectionMade;

        public delegate void DisconnectionNotifier(Server sender, uint connId);
        public event DisconnectionNotifier OnConnectionEnding;
        #endregion

        public Server()
        {
        }

        #region Public Interface
        public string GetUsername(uint connId)
        {
            if (!_connections.ContainsKey(connId))
                return "";
            return _connections[connId].Username;
        }

        public string GetRemoteHost(uint connId)
        {
            if (!_connections.ContainsKey(connId))
                return "";
            return _connections[connId].RemoteHost;
        }

        public void Start()
        {
            if (_running)
                return;
            if (_fs == null)
                throw new FTPException("No filesystem selected.");

            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(IPAddress.Any, _port));
            _listener.Listen(100);

            _dispatcher = new Thread(Dispatcher);
            _running = true;
            _dispatcher.Start(this);
        }

        public void Stop()
        {
            if (!_running)
                return;

            _running = false;

            foreach (Connection conn in _connections.Values)
                conn.Kill();
            _connections.Clear();

            _listener.Close();
            _allDone.Set();
            _dispatcher.Join();
            _dispatcher = null;
        }

        public void Wait()
        {
            if (!_running)
                return;

            _dispatcher.Join();
        }

        public bool ValidateUser(string user, string password)
        {
            if (_mgr == null)
                return true;
            return _mgr.Validate(user, password);
        }

        public bool ValidatePath(string path)
        {
            if (_fs == null || _fs.GetNode("root" + path) == null)
                return false;
            return true;
        }
        #endregion

        #region Private Helpers
        private void TryChange()
        {
            if (_running)
                throw new FTPException("Already running");
        }

        private Connection GetNewConnection(string remoteHost)
        {
            Connection conn = new Connection(this);
            conn.RemoteHost = remoteHost;
            lock (_connections)
            {
                _connections[conn.ID] = conn;
            }
            EmitConnectionMade(conn.ID);
            return conn;
        }

        private void Dispatch()
        {
            while (_running)
            {
                _allDone.Reset();

                _listener.BeginAccept(
                    new AsyncCallback(Connection.AcceptCallback),
                    this);

                _allDone.WaitOne();
            }
        }

        private void Handle(Connection conn, string cmd, string args, out Connection.State nextState, out string msg)
        {
            _handler.Handle(conn, cmd, args, out nextState, out msg);
        }

        private void EmitCommandReceived(uint connId, string cmd, string args)
        {
            if (OnCommandReceived != null)
                OnCommandReceived(this, connId, cmd, args);
        }

        private void EmitResponseSent(uint connId, string msg)
        {
            if (OnResponseSent != null)
                OnResponseSent(this, connId, msg);
        }

        private void EmitConnectionMade(uint connId)
        {
            if (OnConnectionMade != null)
                OnConnectionMade(this, connId);
        }

        private void EmitConnectionEnding(uint connId)
        {
            if (OnConnectionEnding != null)
                OnConnectionEnding(this, connId);
        }

        private void RemoveConnection(uint connId)
        {
            if (_connections.ContainsKey(connId))
            {
                EmitConnectionEnding(connId);
                _connections.Remove(connId);
            }
        }
        #endregion

        #region Callbacks
        public static void Dispatcher(object o)
        {
            Server server = (Server)o;
            server.Dispatch();
        }
        #endregion
    }
}
