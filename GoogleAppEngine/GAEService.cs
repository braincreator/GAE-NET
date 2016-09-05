namespace GoogleAppEngine
{
    public class GAEService
    {
        protected readonly CloudAuthenticator Authenticator;

        protected GAEService(CloudAuthenticator authenticator)
        {
            Authenticator = authenticator;
        }

        public CloudAuthenticator GetAuthenticator()
        {
            return Authenticator;
        }
    }
}
