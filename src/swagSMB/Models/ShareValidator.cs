using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace swagSMB.Models
{
    public static class ShareValidator
    {
        public const int ShareNameMaxLength = 80;

        private static readonly HashSet<string> ReservedShareNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IPC$", "ADMIN$", "PRINT$", "FAX$",
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static bool IsShareNameValid(string shareName, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(shareName))
            {
                reason = "Share name is required.";
                return false;
            }

            if (shareName.Length != shareName.Trim().Length)
            {
                reason = "Share name cannot start or end with whitespace.";
                return false;
            }

            if (shareName.Length > ShareNameMaxLength)
            {
                reason = "Share name must be " + ShareNameMaxLength + " characters or fewer.";
                return false;
            }

            for (int i = 0; i < shareName.Length; i++)
            {
                char c = shareName[i];
                if (char.IsControl(c))
                {
                    reason = "Share name cannot contain control characters.";
                    return false;
                }

                bool letter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
                bool digit = c >= '0' && c <= '9';
                bool ok = letter || digit || c == '_' || c == '-' || c == '.';
                bool dollarTrailing = c == '$' && i == shareName.Length - 1;
                if (!ok && !dollarTrailing)
                {
                    reason = "Share name may only contain letters, digits, '_', '-', '.', and an optional trailing '$'.";
                    return false;
                }
            }

            if (ReservedShareNames.Contains(shareName))
            {
                reason = "'" + shareName + "' is a reserved name and cannot be used.";
                return false;
            }

            return true;
        }

        public static bool TryFindUsernameConflict(IEnumerable<ShareConfig> shares, out string conflictingUsername)
        {
            conflictingUsername = null;
            if (shares == null)
            {
                return false;
            }

            var groups = shares
                .Where(s => s != null && s.Enabled && !string.IsNullOrWhiteSpace(s.Username))
                .GroupBy(s => s.Username, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                string firstPassword = null;
                bool initialized = false;
                foreach (ShareConfig share in group)
                {
                    string password = share.Password ?? string.Empty;
                    if (!initialized)
                    {
                        firstPassword = password;
                        initialized = true;
                        continue;
                    }

                    if (!string.Equals(firstPassword, password, StringComparison.Ordinal))
                    {
                        conflictingUsername = group.Key;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool IsLocalPathSafe(string localPath, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(localPath))
            {
                reason = "Local path is empty.";
                return false;
            }

            string trimmed = localPath.Trim();
            if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                reason = "UNC paths are not allowed.";
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(trimmed);
            }
            catch (Exception ex)
            {
                reason = "Invalid path: " + ex.Message;
                return false;
            }

            string root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || root.StartsWith("\\\\", StringComparison.Ordinal))
            {
                reason = "Path must resolve to a local fixed drive root.";
                return false;
            }

            DirectoryInfo info;
            try
            {
                info = new DirectoryInfo(full);
            }
            catch (Exception ex)
            {
                reason = "Invalid folder: " + ex.Message;
                return false;
            }

            FileAttributes attrs;
            try
            {
                if (!info.Exists)
                {
                    reason = "Folder does not exist.";
                    return false;
                }
                attrs = info.Attributes;
            }
            catch (Exception ex)
            {
                reason = "Could not read folder attributes: " + ex.Message;
                return false;
            }

            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                reason = "Reparse points (junctions/symlinks) are not allowed.";
                return false;
            }

            try
            {
                DriveInfo drive = new DriveInfo(root);
                if (drive.DriveType != DriveType.Fixed)
                {
                    reason = "Path must be on a fixed local drive.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason = "Could not inspect drive: " + ex.Message;
                return false;
            }

            return true;
        }
    }
}
