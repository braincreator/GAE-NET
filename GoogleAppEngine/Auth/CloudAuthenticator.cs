using Google.Apis.Services;

namespace GoogleAppEngine
{
    public abstract class CloudAuthenticator
    {
        public abstract BaseClientService.Initializer GetInitializer();
        public abstract string GetProjectId();
    }
}
