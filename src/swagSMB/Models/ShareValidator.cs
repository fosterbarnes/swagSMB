using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace swagSMB.Models
{
    public static class ShareValidator
    {
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

            if (!Directory.Exists(full))
            {
                reason = "Folder does not exist.";
                return false;
            }

            try
            {
                FileAttributes attrs = File.GetAttributes(full);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    reason = "Reparse points (junctions/symlinks) are not allowed.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason = "Could not read folder attributes: " + ex.Message;
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
