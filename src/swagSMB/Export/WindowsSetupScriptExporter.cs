using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using swagSMB.Models;

namespace swagSMB.Export
{
    internal static class WindowsSetupScriptExporter
    {
        public enum CredentialMode
        {
            EmbedPlaintext,
            PromptAtRuntime
        }

        public static string Build(AppConfig config, IEnumerable<ShareConfig> shares)
        {
            return Build(config, shares, CredentialMode.EmbedPlaintext);
        }

        public static string Build(AppConfig config, IEnumerable<ShareConfig> shares, CredentialMode mode)
        {
            if (config == null || shares == null)
            {
                return null;
            }

            List<ShareConfig> entries = shares
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.ShareName))
                .ToList();

            if (entries.Count == 0)
            {
                return null;
            }

            int port = config.Network != null && config.Network.Port > 0
                ? config.Network.Port
                : NetworkSettings.DefaultPort;

            string host = (config.Network?.BindIPAddress ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(host))
            {
                host = "YOUR_SERVER_IP";
            }

            bool anyAutoDrive = entries.Any(s => string.IsNullOrEmpty(NormalizeMapDriveLetter(s.MapDriveLetter)));

            var sb = new StringBuilder();
            if (anyAutoDrive)
            {
                sb.AppendLine("function swagSMB_NextUnusedDriveLetter {");
                sb.AppendLine("  $inUse = (Get-PSDrive -PSProvider FileSystem).Name");
                sb.AppendLine("  for ($code = [int][char]'Z'; $code -ge [int][char]'D'; $code--) {");
                sb.AppendLine("    $n = [string][char]$code");
                sb.AppendLine("    if ($n -notin $inUse) { return $n + ':' }");
                sb.AppendLine("  }");
                sb.AppendLine("  throw 'No free drive letters D: through Z:.'");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            if (mode == CredentialMode.PromptAtRuntime)
            {
                sb.AppendLine("$swagSmbCredCache = @{}");
                sb.AppendLine("function swagSMB_GetCred([string]$user) {");
                sb.AppendLine("  if (-not $swagSmbCredCache.ContainsKey($user)) {");
                sb.AppendLine("    $swagSmbCredCache[$user] = Get-Credential -UserName $user -Message ('Enter password for swagSMB user: ' + $user)");
                sb.AppendLine("  }");
                sb.AppendLine("  return $swagSmbCredCache[$user]");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            foreach (ShareConfig s in entries)
            {
                string shareEsc = PsSingleQuote(s.ShareName.Trim());
                string localPath = FormatLocalPathArg(s);
                sb.Append("New-SmbMapping -LocalPath ");
                sb.Append(localPath);
                sb.Append(" -RemotePath ");
                sb.Append("'\\\\");
                sb.Append(host);
                sb.Append('\\');
                sb.Append(shareEsc);
                sb.Append("' -TcpPort ");
                sb.Append(port);

                if (mode == CredentialMode.PromptAtRuntime)
                {
                    sb.Append(" -UserName ");
                    sb.Append(Utf8Base64DecodedExpression((s.Username ?? string.Empty).Trim()));
                    sb.Append(" -Password (");
                    sb.Append("(swagSMB_GetCred ");
                    sb.Append(Utf8Base64DecodedExpression((s.Username ?? string.Empty).Trim()));
                    sb.AppendLine(").GetNetworkCredential().Password)");
                }
                else
                {
                    sb.Append(" -UserName ");
                    sb.Append(Utf8Base64DecodedExpression((s.Username ?? string.Empty).Trim()));
                    sb.Append(" -Password ");
                    sb.AppendLine(Utf8Base64DecodedExpression(s.Password ?? string.Empty));
                }
            }

            AppendExplorerShellFooter(sb);
            return sb.ToString();
        }

        public static string BuildRemoval(AppConfig config, IEnumerable<ShareConfig> shares)
        {
            if (config == null || shares == null)
            {
                return null;
            }

            List<ShareConfig> entries = shares
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.ShareName))
                .ToList();

            if (entries.Count == 0)
            {
                return null;
            }

            string host = (config.Network?.BindIPAddress ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(host))
            {
                host = "YOUR_SERVER_IP";
            }

            var sb = new StringBuilder();
            sb.AppendLine("# Disconnect mappings for these shares (errors ignored if not connected)");
            foreach (ShareConfig s in entries)
            {
                string shareEsc = PsSingleQuote(s.ShareName.Trim());
                sb.Append("Remove-SmbMapping -RemotePath ");
                sb.Append("'\\\\");
                sb.Append(host);
                sb.Append('\\');
                sb.Append(shareEsc);
                sb.AppendLine("' -Force -ErrorAction SilentlyContinue");
            }

            AppendExplorerShellFooter(sb);
            return sb.ToString();
        }

        private static void AppendExplorerShellFooter(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue");
            sb.AppendLine("Start-Sleep -Milliseconds 600");
            sb.AppendLine("Start-Process explorer.exe");
        }

        private static string FormatLocalPathArg(ShareConfig s)
        {
            string letter = NormalizeMapDriveLetter(s.MapDriveLetter);
            if (string.IsNullOrEmpty(letter))
            {
                return "(swagSMB_NextUnusedDriveLetter)";
            }

            return "'" + PsSingleQuote(letter) + "'";
        }

        private static string NormalizeMapDriveLetter(string value)
        {
            string t = value?.Trim() ?? string.Empty;
            if (t.Length >= 2 && t.EndsWith(":", System.StringComparison.Ordinal))
            {
                t = t.Substring(0, 1);
            }

            if (t.Length != 1)
            {
                return string.Empty;
            }

            char c = char.ToUpperInvariant(t[0]);
            return c >= 'D' && c <= 'Z' ? c + ":" : string.Empty;
        }

        private static string PsSingleQuote(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        private static string Utf8Base64DecodedExpression(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
            {
                return "''";
            }

            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
            return "([System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('" + b64 + "')))";
        }
    }
}
