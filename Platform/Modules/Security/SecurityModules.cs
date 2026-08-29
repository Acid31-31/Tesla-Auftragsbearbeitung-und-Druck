using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PDF_umbenenen.Platform.Contracts;
using PDF_umbenenen.Platform.Models;

namespace PDF_umbenenen.Platform.Modules.Security
{
    internal sealed class UserManagerModule : IUserManager
    {
        private PlatformUser _current;

        public bool Authenticate(string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return false;

            var valid = !string.IsNullOrWhiteSpace(password) && password.Length >= 1;
            if (!valid)
                return false;

            _current = new PlatformUser
            {
                UserName = userName,
                DisplayName = userName,
                RoleId = string.Equals(userName, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "operator",
                IsAuthenticated = true
            };

            return true;
        }

        public PlatformUser GetCurrentUser()
            => _current ?? new PlatformUser { UserName = Environment.UserName, DisplayName = Environment.UserName, RoleId = "operator", IsAuthenticated = true };

        public void SetCurrentUser(PlatformUser user) => _current = user;
    }

    internal sealed class RoleManagerModule : IRoleManager
    {
        private readonly Dictionary<string, PlatformRole> _roles =
            new Dictionary<string, PlatformRole>(StringComparer.OrdinalIgnoreCase)
            {
                ["admin"] = new PlatformRole
                {
                    RoleId = "admin",
                    DisplayName = "Administrator",
                    Permissions = new List<string> { "read", "write", "delete", "configure", "backup", "restore", "api" }
                },
                ["operator"] = new PlatformRole
                {
                    RoleId = "operator",
                    DisplayName = "Bediener",
                    Permissions = new List<string> { "read", "write", "process" }
                },
                ["viewer"] = new PlatformRole
                {
                    RoleId = "viewer",
                    DisplayName = "Betrachter",
                    Permissions = new List<string> { "read" }
                }
            };

        public bool HasPermission(string roleId, string permission)
        {
            if (string.IsNullOrWhiteSpace(roleId) || string.IsNullOrWhiteSpace(permission))
                return false;

            return _roles.TryGetValue(roleId, out var role)
                   && role.Permissions.Any(p => p.Equals(permission, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> GetPermissions(string roleId)
            => _roles.TryGetValue(roleId, out var role)
                ? (IReadOnlyList<string>)role.Permissions
                : Array.Empty<string>();
    }

    internal sealed class DataEncryptionModule : IDataEncryption
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DOK-V01-Platform-Local");

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            var data = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            var data = Convert.FromBase64String(cipherText);
            var plain = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
    }

    internal sealed class PlatformBackupServiceModule : IPlatformBackupService
    {
        public string CreateBackup(string targetFolder)
        {
            if (string.IsNullOrWhiteSpace(targetFolder))
                targetFolder = Path.Combine(AppInfo.UserDataDirectory, "Backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));

            Directory.CreateDirectory(targetFolder);
            CopyTree(AppInfo.UserDataDirectory, Path.Combine(targetFolder, "UserData"));
            return targetFolder;
        }

        public void Restore(string backupFolder)
        {
            if (!Directory.Exists(backupFolder))
                return;

            var source = Path.Combine(backupFolder, "UserData");
            if (!Directory.Exists(source))
                source = backupFolder;

            CopyTree(source, AppInfo.UserDataDirectory);
        }

        private static void CopyTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var dest = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest, true);
            }
        }
    }
}
