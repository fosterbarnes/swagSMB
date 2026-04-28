using swagSMB.Security;

namespace swagSMB.Models
{
    public sealed class SessionContext
    {
        public SecureMasterSecret MasterSecret { get; } = new SecureMasterSecret();
        public AppConfig Config { get; set; } = new AppConfig();

        public string MasterPassword
        {
            get => MasterSecret.AsTransientString();
            set => MasterSecret.Set(value ?? string.Empty);
        }
    }
}
