using System;
using System.Collections.Generic;
using System.Text;
using BasicFS;
using DataFS;
using System.IO;
using Cipher;

namespace Test
{
    class Program
    {
        static void PrintDir(FileSystemNode node)
        {
            foreach (FileSystemNode child in node.GetChildren())
            {
                Console.WriteLine(child.Path);
                if (child.ChildCount > 0)
                    PrintDir(child);
            }
        }

        static void CommandReceived(SimpleFTP.Server sender, uint connId, string cmd, string args)
        {
            Console.WriteLine("CMMD {0}: \"{1} {2}\"", sender.GetRemoteHost(connId), cmd, args);
        }

        static void ResponseSent(SimpleFTP.Server sender, uint connId, string msg)
        {
            Console.WriteLine("RESP {0}: \"{1}\"", sender.GetRemoteHost(connId), msg);
        }

        static void Main(string[] args)
        {
#if false
            //SystemFileSystem fs = new SystemFileSystem(new DirectoryInfo(@"C:\Corey Derochie"));
            CipherStream stream = new CipherStream(
                new FileStream(@"C:\Corey\TestOut1.dat", FileMode.Open, FileAccess.Read),
                new FileStream(@"C:\Corey\TestOut2.dat", FileMode.Open, FileAccess.Read));
            using (DataFileSystem fs = new DataFileSystem(stream))  //.CreateAndLoad(new DirectoryInfo(@"C:\Corey Derochie"), stream))
            {
                //PrintDir(fs.Root);
                FileSystemNode node = fs.GetNode("root/test1/test2/Capn Log.txt");
                StreamReader reader = new StreamReader(fs.GetReadableStream(node));
                string contents = reader.ReadToEnd();
                Console.WriteLine(contents);
            }
#endif

            SystemFileSystem fs = new SystemFileSystem(new DirectoryInfo(@"F:\temp"));
            SimpleFTP.Server server = new SimpleFTP.Server();
            server.FileSystem = fs;
            server.Handler = new SimpleFTP.BasicCommandHandler();
            server.OnCommandReceived += new SimpleFTP.Server.CommandNotifier(CommandReceived);
            server.OnResponseSent += new SimpleFTP.Server.ResponseNotifier(ResponseSent);
            server.OnConnectionMade += new SimpleFTP.Server.ConnectionNotifier(ConnectionMade);
            server.OnConnectionEnding += new SimpleFTP.Server.DisconnectionNotifier(ConnectionEnding);
            server.Start();

            //Console.Write("Press any key...");
            Console.ReadLine();

            server.Stop();
        }

        static void ConnectionEnding(SimpleFTP.Server sender, uint connId)
        {
            Console.WriteLine("\"{0}\" is disconnecting.", sender.GetRemoteHost(connId));
        }

        static void ConnectionMade(SimpleFTP.Server sender, uint connId)
        {
            Console.WriteLine("\"{0}\" has connected.", sender.GetRemoteHost(connId));
        }
    }
}
