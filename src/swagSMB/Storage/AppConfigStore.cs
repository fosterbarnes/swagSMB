using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using swagSMB.Models;
using swagSMB.Security;

namespace swagSMB.Storage
{
    public sealed class TrayStartupFlags
    {
        public bool StartMinimizedToTray { get; set; }
        public bool RequireMasterPasswordWhenStartingToTray { get; set; }
        public bool AutoTrayConsented { get; set; }
    }

    public sealed class AppConfigStore
    {
        private const int DpapiEntropySize = 32;
        private static readonly byte[] LegacyDpapiEntropy = Encoding.ASCII.GetBytes("swagSMB.tray.v1");
        private static readonly JsonSerializerSettings UiPreferencesSerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 32,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            Converters = { new StringEnumConverter() }
        };

        private static readonly JsonSerializerSettings StrictDeserializeSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 32,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore
        };

        private readonly string _baseDirectory;
        private readonly string _configFilePath;
        private readonly string _trayFlagsFilePath;
        private readonly string _trayKeyFilePath;
        private readonly string _trayEntropyFilePath;
        private readonly string _uiPreferencesFilePath;

        public AppConfigStore()
        {
            _baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "swagSMB");
            _configFilePath = Path.Combine(_baseDirectory, "config.secure");
            _trayFlagsFilePath = Path.Combine(_baseDirectory, "tray.json");
            _trayKeyFilePath = Path.Combine(_baseDirectory, "tray.key");
            _trayEntropyFilePath = Path.Combine(_baseDirectory, "tray.entropy");
            _uiPreferencesFilePath = Path.Combine(_baseDirectory, "ui.json");
        }

        public string ConfigFilePath => _configFilePath;

        public bool ConfigExists()
        {
            return File.Exists(_configFilePath);
        }

        public void DeleteConfig()
        {
            TryDelete(_configFilePath);
            TryDelete(_trayFlagsFilePath);
            TryDelete(_trayKeyFilePath);
            TryDelete(_trayEntropyFilePath);
        }

        private byte[] LoadOrCreateTrayEntropy()
        {
            try
            {
                if (File.Exists(_trayEntropyFilePath))
                {
                    byte[] existing = File.ReadAllBytes(_trayEntropyFilePath);
                    if (existing.Length == DpapiEntropySize)
                    {
                        return existing;
                    }
                }
            }
            catch
            {
            }

            byte[] fresh = new byte[DpapiEntropySize];
            System.Security.Cryptography.RandomNumberGenerator.Fill(fresh);
            try
            {
                Directory.CreateDirectory(_baseDirectory);
                File.WriteAllBytes(_trayEntropyFilePath, fresh);
            }
            catch
            {
            }

            return fresh;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public TrayStartupFlags LoadTrayFlags()
        {
            try
            {
                if (!File.Exists(_trayFlagsFilePath))
                {
                    return null;
                }

                string json = File.ReadAllText(_trayFlagsFilePath);
                return JsonConvert.DeserializeObject<TrayStartupFlags>(json, StrictDeserializeSettings);
            }
            catch
            {
                return null;
            }
        }

        public void SaveTrayFlags(TrayStartupFlags flags)
        {
            if (flags == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_baseDirectory);
                string json = JsonConvert.SerializeObject(flags, Formatting.Indented);
                File.WriteAllText(_trayFlagsFilePath, json);
            }
            catch
            {
            }
        }

        public void DeleteTrayFlags()
        {
            TryDelete(_trayFlagsFilePath);
        }

        public UiPreferences LoadUiPreferences()
        {
            try
            {
                if (!File.Exists(_uiPreferencesFilePath))
                {
                    return new UiPreferences();
                }

                string json = File.ReadAllText(_uiPreferencesFilePath);
                var jo = JObject.Parse(json);
                if (jo["Theme"] != null)
                {
                    JToken t = jo["Theme"];
                    if (t.Type == JTokenType.String && Enum.TryParse(t.ToString(), true, out UiThemeKind byName))
                    {
                        return new UiPreferences { Theme = byName };
                    }
                    if (t.Type == JTokenType.Integer)
                    {
                        return new UiPreferences { Theme = MapLegacyThemeInt(t.Value<int>()) };
                    }
                }
                if (jo["DarkMode"]?.Type == JTokenType.Boolean && jo["DarkMode"].Value<bool>())
                {
                    return new UiPreferences { Theme = UiThemeKind.Dark };
                }
            }
            catch
            {
            }
            return new UiPreferences();
        }

        public void SaveUiPreferences(UiPreferences preferences)
        {
            if (preferences == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_baseDirectory);
                string json = JsonConvert.SerializeObject(preferences, UiPreferencesSerializerSettings);
                File.WriteAllText(_uiPreferencesFilePath, json);
            }
            catch
            {
            }
        }

        private static UiThemeKind MapLegacyThemeInt(int v)
        {
            return v switch
            {
                0 => UiThemeKind.Light,
                1 => UiThemeKind.Dark,
                2 => UiThemeKind.Dracula,
                _ => UiThemeKind.System
            };
        }

        public void SaveProtectedMasterPassword(string masterPassword)
        {
            if (string.IsNullOrEmpty(masterPassword))
            {
                DeleteProtectedMasterPassword();
                return;
            }

            try
            {
                Directory.CreateDirectory(_baseDirectory);
                byte[] entropy = LoadOrCreateTrayEntropy();
                byte[] plain = Encoding.UTF8.GetBytes(masterPassword);
                byte[] protectedBytes = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_trayKeyFilePath, protectedBytes);
                Array.Clear(plain, 0, plain.Length);
            }
            catch
            {
                TryDelete(_trayKeyFilePath);
            }
        }

        public bool TryLoadProtectedMasterPassword(out string masterPassword)
        {
            masterPassword = null;
            try
            {
                if (!File.Exists(_trayKeyFilePath))
                {
                    return false;
                }

                byte[] protectedBytes = File.ReadAllBytes(_trayKeyFilePath);
                byte[] plain = TryUnprotectWithCurrentOrLegacyEntropy(protectedBytes);
                if (plain == null)
                {
                    return false;
                }

                masterPassword = Encoding.UTF8.GetString(plain);
                Array.Clear(plain, 0, plain.Length);
                return !string.IsNullOrEmpty(masterPassword);
            }
            catch
            {
                return false;
            }
        }

        private byte[] TryUnprotectWithCurrentOrLegacyEntropy(byte[] protectedBytes)
        {
            try
            {
                byte[] entropy = LoadOrCreateTrayEntropy();
                return ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
            }
            catch
            {
            }

            try
            {
                return ProtectedData.Unprotect(protectedBytes, LegacyDpapiEntropy, DataProtectionScope.CurrentUser);
            }
            catch
            {
                return null;
            }
        }

        public void DeleteProtectedMasterPassword()
        {
            TryDelete(_trayKeyFilePath);
        }

        public bool TryVerifyMasterPassword(string masterPassword)
        {
            if (!ConfigExists() || string.IsNullOrEmpty(masterPassword))
            {
                return false;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                byte[] encrypted = File.ReadAllBytes(_configFilePath);
                return SecretsCrypto.VerifyMasterPassword(masterPassword, encrypted);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                stopwatch.Stop();
                Debug.WriteLine($"[Perf] TryVerifyMasterPassword took {stopwatch.ElapsedMilliseconds} ms.");
            }
        }

        public AppConfig Load(string masterPassword)
        {
            var stopwatch = Stopwatch.StartNew();
            byte[] encrypted = File.ReadAllBytes(_configFilePath);
            byte[] plainBytes = SecretsCrypto.Decrypt(masterPassword, encrypted);
            string json = Encoding.UTF8.GetString(plainBytes);
            AppConfig config = JsonConvert.DeserializeObject<AppConfig>(json, StrictDeserializeSettings) ?? new AppConfig();
            if (config.Shares == null)
            {
                config.Shares = new System.Collections.Generic.List<ShareConfig>();
            }

            stopwatch.Stop();
            Debug.WriteLine($"[Perf] Load took {stopwatch.ElapsedMilliseconds} ms (encrypted bytes: {encrypted.Length}).");
            return config;
        }

        public void Save(string masterPassword, AppConfig config)
        {
            var stopwatch = Stopwatch.StartNew();
            Directory.CreateDirectory(_baseDirectory);
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = SecretsCrypto.Encrypt(masterPassword, plainBytes);
            WriteAllBytesAtomically(_configFilePath, encrypted);

            if (config != null && config.Server != null)
            {
                SaveTrayFlags(new TrayStartupFlags
                {
                    StartMinimizedToTray = config.Server.StartMinimizedToTray,
                    RequireMasterPasswordWhenStartingToTray = config.Server.RequireMasterPasswordWhenStartingToTray,
                    AutoTrayConsented = config.Server.AutoTrayConsented
                });
            }

            stopwatch.Stop();
            Debug.WriteLine($"[Perf] Save took {stopwatch.ElapsedMilliseconds} ms (encrypted bytes: {encrypted.Length}).");
        }

        private static void WriteAllBytesAtomically(string path, byte[] contents)
        {
            string tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, contents);

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
                return;
            }

            File.Move(tempPath, path);
        }
    }
}
