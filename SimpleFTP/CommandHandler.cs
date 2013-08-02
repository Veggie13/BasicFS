using System.Collections.Generic;
using BasicFS;
using System.IO;
using System;
using System.Text;

namespace SimpleFTP
{

    public partial class Server
    {
        public interface ICommandHandler
        {
            void Handle(Connection conn, string cmd, string args, out Connection.State nextState, out string msg);
        }

        private class DefaultCommandHandler : ICommandHandler
        {
            public void Handle(Connection conn, string cmd, string args, out Connection.State nextState, out string msg)
            {
                nextState = Connection.State.Idle;
                msg = "550 Command not implemented.";
            }
        }
    }

    public class BasicCommandHandler : Server.ICommandHandler
    {
        private static Dictionary<Server.Connection.State, Server.ICommandHandler> StateHandlers;
        static BasicCommandHandler()
        {
            StateHandlers = new Dictionary<Server.Connection.State, Server.ICommandHandler>();
            StateHandlers[Server.Connection.State.Login] = new LoginHandler();
            StateHandlers[Server.Connection.State.Idle] = new IdleHandler();
            StateHandlers[Server.Connection.State.List] = new BusyHandler();
            StateHandlers[Server.Connection.State.Download] = new BusyHandler();
            StateHandlers[Server.Connection.State.Upload] = new BusyHandler();
        }

        public void Handle(Server.Connection conn, string cmd, string args, out Server.Connection.State nextState, out string msg)
        {
            nextState = conn.ConnectionState;

            switch (cmd)
            {
                case "QUIT":
                case "BYE":
                    msg = "220 " + conn.Server.GoodbyeMessage;
                    conn.Kill();
                    return;
                case "TYPE":
                case "NOOP":
                    msg = "200 Heyo!";
                    return;
                case "PASV":
                case "STOR":
                case "DELE":
                case "RMD":
                case "MKD":
                case "SYST":
                    msg = "215 UNIX emulated by Quick 'n Easy FTP Server.";
                    return;
                default:
                    StateHandlers[conn.ConnectionState].Handle(conn, cmd, args, out nextState, out msg);
                    return;
            }
        }

        private class LoginHandler : Server.ICommandHandler
        {
            public void Handle(Server.Connection conn, string cmd, string args, out Server.Connection.State nextState, out string msg)
            {
                nextState = Server.Connection.State.Login;
                switch (cmd)
                {
                    case "USER":
                        conn.Username = args;
                        msg = "331 Password required for " + args;
                        return;
                    case "PASS":
                        if (conn.Username.Equals(""))
                        {
                            msg = "503 Login with USER first.";
                            return;
                        }
                        if (conn.Server.ValidateUser(conn.Username, args))
                        {
                            nextState = Server.Connection.State.Idle;
                            msg = "230 User successfully logged in.";
                            conn.CurrentDirectory = "/";
                            return;
                        }
                        msg = "530 Not logged in, user or password incorrect!";
                        return;
                    default:
                        msg = "530 Please login with USER and PASS.";
                        return;
                }
            }
        }

        private class IdleHandler : Server.ICommandHandler
        {
            public void Handle(Server.Connection conn, string cmd, string args, out Server.Connection.State nextState, out string msg)
            {
                nextState = Server.Connection.State.Idle;
                switch (cmd)
                {
                    case "PWD":
                        msg = "257 \"" + conn.CurrentDirectory + "\" is the current directory.";
                        return;
                    case "CDUP":
                        if (conn.ChangeDirectory(args))
                            msg = "250 \"" + conn.CurrentDirectory + "\" is the current directory.";
                        else
                            msg = "550 Could not access upper directory.";
                        return;
                    case "CWD":
                        if (conn.ChangeDirectory(args))
                            msg = "250 \"" + conn.CurrentDirectory + "\" is the current directory.";
                        else
                            msg = "550 Could not access directory.";
                        return;
                    case "PORT":
                    {
                        string[] parts = args.Split(',');
                        if (parts.Length != 6)
                        {
                            msg = "550 Invalid port argument.";
                            return;
                        }

                        conn.RemoteHost = string.Join(".", parts[0], parts[1], parts[2], parts[3]);

                        int newPort = conn.Port, p1 = 0, p2 = 0;
                        if (int.TryParse(parts[4], out p1) && int.TryParse(parts[5], out p2))
                        {
                            conn.Port = 256 * p1 + p2;
                            msg = "200 Port set successfully.";
                        }
                        else
                        {
                            msg = "550 Invalid port argument.";
                        }
                        return;
                    }
                    case "LIST":
                    {
                        string path = "root" + conn.ResolveRelativePath(args);
                        FileSystemNode node = conn.Server.FileSystem.GetNode(path);
                        FileSystemNode[] children = conn.Server.FileSystem.LoadChildren(node, 1);
                        MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(FormatList(children)));
                        if (conn.CreateDataJob(stream))
                        {
                            msg = "150 Opening ASCII mode data connection for directory list.";
                            nextState = Server.Connection.State.List;
                        }
                        else
                        {
                            msg = "550 Failed to open data connection.";
                        }
                        return;
                    }
                    case "RETR":
                    {
                        msg = "550";
                        return;
                    }
                    case "SIZE":
                    {
                        string path = "root" + conn.ResolveRelativePath(args);
                        FileSystemNode node = conn.Server.FileSystem.GetNode(path);
                        ulong size = conn.Server.FileSystem.GetFileSize(node);
                        msg = string.Format("213 {0}", size);
                        return;
                    }
                    default:
                        msg = "550 Feature \"" + cmd + "\" not available.";
                        return;
                }
            }
        }

        private class BusyHandler : Server.ICommandHandler
        {
            public void Handle(Server.Connection conn, string cmd, string args, out Server.Connection.State nextState, out string msg)
            {
                nextState = conn.ConnectionState;
                msg = "550 Server is busy.";
            }
        }

        private static string FormatList(FileSystemNode[] list)
        {
            string result = "";
            foreach (FileSystemNode node in list)
            {
                result += node.Name + "\r\n";
            }
            return result;
        }
    }

}