namespace SimpleFTP
{

    public partial class Server
    {
        public interface IUserManager
        {
            bool Validate(string user, string password);
        }
    }

}