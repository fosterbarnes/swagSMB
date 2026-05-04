using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using SMBLibrary;
using SMBLibrary.Authentication.GSSAPI;
using SMBLibrary.Authentication.NTLM;
using SMBLibrary.Server;
using SMBLibrary.Win32;
using swagSMB.Models;
using Utilities;

namespace swagSMB.Core
{
    public sealed class SmbServerHost
    {
        private ConfigurableSmbServer _server;
        private AppConfig _config;
        private string _libraryLogListenEndpoint;

        public bool IsRunning { get; private set; }

        public event Action<string> ServerActivity;

        public void Start(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Stop();

            _config = config;

            if (ShareValidator.TryFindUsernameConflict(config.Shares, out string conflictUser))
            {
                throw new InvalidOperationException(
                    "Two or more enabled shares use the username '" + conflictUser +
                    "' with different passwords. Resolve the conflict in the Shares tab before starting the server.");
            }

            List<ShareConfig> enabledShares = GetEnabledShares(config, logDiagnostics: true);
            SMBShareCollection shares = BuildShares(enabledShares);
            var authProvider = new SecureNtlmAuthenticationProvider(GetUserPasswordForAuth);
            var gssProvider = new GSSProvider(authProvider);
            _server = new ConfigurableSmbServer(shares, gssProvider);

            AttachServerEvents(_server);

            IPAddress bindAddress = ResolveBindAddress(config.Network);
            int port = config.Network.Port <= 0 ? NetworkSettings.DefaultPort : config.Network.Port;
            string bindLabel = bindAddress.Equals(IPAddress.Any)
                ? "0.0.0.0 (all interfaces)"
                : bindAddress.ToString();

            try
            {
                RaiseActivity(string.Format(
                    "[{0:HH:mm:ss}] [Server] Starting SMB listener on {1}, port {2}, Direct TCP (SMB2/SMB3)...",
                    DateTime.Now,
                    bindLabel,
                    port));

                RaiseActivity(string.Format(
                    "[{0:HH:mm:ss}] [Server] SMB listener started.",
                    DateTime.Now));

                _libraryLogListenEndpoint = FormatListenEndpoint(bindAddress, port);

                _server.StartWithPort(
                    bindAddress,
                    SMBTransportType.DirectTCPTransport,
                    port,
                    false,
                    true,
                    true);

                RaiseActivity(string.Format(
                    "[{0:HH:mm:ss}] [SMB Server] [Information] SMB server started.",
                    DateTime.Now));

                IsRunning = true;
            }
            catch
            {
                CleanupFailedStart();
                throw;
            }
        }

        private void CleanupFailedStart()
        {
            if (_server == null)
            {
                return;
            }

            SMBServer server = _server;
            DetachServerEvents(server);
            try
            {
                server.Stop();
            }
            catch
            {
            }

            _server = null;
            IsRunning = false;
            _libraryLogListenEndpoint = null;
        }

        public void Stop()
        {
            if (_server != null)
            {
                SMBServer server = _server;
                server.Stop();
                WaitForSessionsDrain(server, TimeSpan.FromMilliseconds(750));
                DetachServerEvents(server);
                _server = null;
            }

            IsRunning = false;
            _libraryLogListenEndpoint = null;
        }

        private static void WaitForSessionsDrain(SMBServer server, TimeSpan budget)
        {
            try
            {
                var deadline = DateTime.UtcNow + budget;
                while (DateTime.UtcNow < deadline)
                {
                    var sessions = server.GetSessionsInformation();
                    if (sessions == null || sessions.Count == 0)
                    {
                        return;
                    }

                    System.Threading.Thread.Sleep(50);
                }
            }
            catch
            {
            }
        }

        public void Restart(AppConfig config)
        {
            Stop();
            Start(config);
        }

        private void AttachServerEvents(SMBServer server)
        {
            server.LogEntryAdded += OnServerLogEntryAdded;
            server.ConnectionRequested += OnConnectionRequested;
        }

        private void DetachServerEvents(SMBServer server)
        {
            server.LogEntryAdded -= OnServerLogEntryAdded;
            server.ConnectionRequested -= OnConnectionRequested;
        }

        private void OnServerLogEntryAdded(object sender, EventArgs e)
        {
            var entry = (LogEntry)e;
            if (entry.Severity > Severity.Information)
            {
                return;
            }

            string message = entry.Message ?? string.Empty;
            if (message == "Starting server" && !string.IsNullOrEmpty(_libraryLogListenEndpoint))
            {
                message = "Starting SMB server at " + _libraryLogListenEndpoint;
            }

            RaiseActivity(string.Format(
                "[{0:HH:mm:ss}] [{1}] [{2}] {3}",
                entry.Time,
                SanitizeLogField(entry.Source),
                entry.Severity,
                SanitizeLogField(message)));
        }

        private void OnConnectionRequested(object sender, EventArgs e)
        {
            var args = (ConnectionRequestEventArgs)e;
            string decision = args.Accept ? "accepted" : "rejected";
            RaiseActivity(string.Format(
                "[{0:HH:mm:ss}] [Connection] Incoming {1}:{2} ({3})",
                DateTime.Now,
                SanitizeLogField(args.IPEndPoint.Address?.ToString()),
                args.IPEndPoint.Port,
                decision));
        }

        private const int MaxLogFieldLength = 1024;

        private static string SanitizeLogField(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c == '\r' || c == '\n' || c == '\t' || char.IsControl(c))
                {
                    chars[i] = ' ';
                }
            }

            string sanitized = new string(chars);
            return sanitized.Length > MaxLogFieldLength
                ? sanitized.Substring(0, MaxLogFieldLength) + "..."
                : sanitized;
        }

        private List<ShareConfig> GetEnabledShares(AppConfig config, bool logDiagnostics)
        {
            var validShares = new List<ShareConfig>();
            if (config.Shares == null)
            {
                return validShares;
            }

            foreach (ShareConfig share in config.Shares.Where(item => item.Enabled))
            {
                if (string.IsNullOrWhiteSpace(share.ShareName) || string.IsNullOrWhiteSpace(share.LocalPath))
                {
                    if (logDiagnostics)
                    {
                        RaiseActivity(string.Format(
                            "[{0:HH:mm:ss}] [Share] Skipped (missing name or path), Id={1}",
                            DateTime.Now,
                            share.Id));
                    }
                    continue;
                }

                string trimmedName = share.ShareName.Trim();
                if (!ShareValidator.IsShareNameValid(trimmedName, out string nameReason))
                {
                    if (logDiagnostics)
                    {
                        RaiseActivity(string.Format(
                            "[{0:HH:mm:ss}] [Share] Skipped '{1}': {2}",
                            DateTime.Now,
                            share.ShareName,
                            nameReason));
                    }
                    continue;
                }

                if (!ShareValidator.IsLocalPathSafe(share.LocalPath, out string pathReason))
                {
                    if (logDiagnostics)
                    {
                        RaiseActivity(string.Format(
                            "[{0:HH:mm:ss}] [Share] Skipped '{1}': {2} ({3})",
                            DateTime.Now,
                            share.ShareName,
                            pathReason,
                            share.LocalPath));
                    }
                    continue;
                }

                if (logDiagnostics)
                {
                    RaiseActivity(string.Format(
                        "[{0:HH:mm:ss}] [Share] Registered '{1}' -> {2}",
                        DateTime.Now,
                        share.ShareName,
                        share.LocalPath));
                }

                validShares.Add(share);
            }

            return validShares;
        }

        private void RaiseActivity(string line)
        {
            ServerActivity?.Invoke(line);
        }

        private SMBShareCollection BuildShares(IReadOnlyList<ShareConfig> enabledShares)
        {
            var collection = new SMBShareCollection();

            foreach (ShareConfig shareConfig in enabledShares)
            {
                var share = new FileSystemShare(shareConfig.ShareName, new NTDirectoryFileSystem(shareConfig.LocalPath));
                share.AccessRequested += delegate(object sender, AccessRequestArgs args)
                {
                    bool allowUser = string.Equals(args.UserName, shareConfig.Username, StringComparison.OrdinalIgnoreCase);
                    args.Allow = allowUser;
                };

                collection.Add(share);
            }

            return collection;
        }

        private string GetUserPasswordForAuth(string userName)
        {
            if (_config?.Shares == null || string.IsNullOrWhiteSpace(userName))
            {
                return null;
            }

            string firstPassword = null;
            bool found = false;
            foreach (ShareConfig share in _config.Shares)
            {
                if (!share.Enabled || !string.Equals(share.Username, userName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!found)
                {
                    firstPassword = share.Password;
                    found = true;
                    continue;
                }

                if (!string.Equals(share.Password, firstPassword, StringComparison.Ordinal))
                {
                    return null;
                }
            }

            return found ? firstPassword : null;
        }

        private static IPAddress ResolveBindAddress(NetworkSettings settings)
        {
            if (settings == null || settings.ListenOnAllInterfaces)
            {
                return IPAddress.Any;
            }

            if (!string.IsNullOrWhiteSpace(settings.BindIPAddress) && IPAddress.TryParse(settings.BindIPAddress, out IPAddress address))
            {
                return address;
            }

            return IPAddress.Any;
        }

        private static string FormatListenEndpoint(IPAddress address, int port)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return "[" + address.ToString() + "]:" + port;
            }

            return address.ToString() + ":" + port;
        }

        private sealed class ConfigurableSmbServer : SMBServer
        {
            public ConfigurableSmbServer(SMBShareCollection shares, GSSProvider securityProvider) : base(shares, securityProvider)
            {
            }

            public void StartWithPort(IPAddress serverAddress, SMBTransportType transport, int port, bool enableSMB1, bool enableSMB2, bool enableSMB3)
            {
                Start(serverAddress, transport, port, enableSMB1, enableSMB2, enableSMB3, null);
            }
        }
    }
}
