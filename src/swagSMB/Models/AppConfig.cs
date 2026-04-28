using System;
using System.Collections.Generic;

namespace swagSMB.Models
{
    public sealed class AppConfig
    {
        public NetworkSettings Network { get; set; } = new NetworkSettings();
        public SecuritySettings Security { get; set; } = new SecuritySettings();
        public ServerSettings Server { get; set; } = new ServerSettings();
        public List<ShareConfig> Shares { get; set; } = new List<ShareConfig>();
    }

    public sealed class ServerSettings
    {
        public bool AutoStartAfterUnlock { get; set; } = true;
        public bool StartWithWindows { get; set; }
        public bool StartMinimizedToTray { get; set; }
        public bool CloseToTray { get; set; }
        public bool RequireMasterPasswordWhenStartingToTray { get; set; } = true;
        public bool AutoTrayConsented { get; set; }
    }

    public sealed class NetworkSettings
    {
        public const int DefaultPort = 5446;

        public bool ListenOnAllInterfaces { get; set; } = false;
        public string BindIPAddress { get; set; } = string.Empty;
        public int Port { get; set; } = DefaultPort;
    }

    public sealed class SecuritySettings
    {
        public bool RequireSigning { get; set; } = true;
        public bool DefaultRequireEncryption { get; set; } = true;
        public bool LockProtocolToSmb21AndSmb30 { get; set; } = true;
        public int AutoLockMinutes { get; set; } = 15;
    }

    public sealed class ShareConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string ShareName { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ProtocolMode { get; set; } = "SMB3.0";
        public bool RequireEncryption { get; set; } = true;
        public bool Enabled { get; set; } = true;

        public string MapDriveLetter { get; set; } = string.Empty;
    }
}
