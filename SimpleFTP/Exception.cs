using System;

namespace SimpleFTP
{
    public partial class Server
    {
        public class FTPException : Exception
        {
            public FTPException(string msg)
                : base(msg)
            {
            }
        }
    }
}