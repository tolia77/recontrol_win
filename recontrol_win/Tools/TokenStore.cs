using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace recontrol_win.Tools
{
    /// <summary>
    /// Holds token information to persist.
    /// </summary>
    public sealed record TokenData(string UserId, string DeviceId, string AccessToken, string RefreshToken);

    /// <summary>
    /// Stores and retrieves TokenData using Windows DPAPI (ProtectedData) in the user's AppData folder.
    /// </summary>
    public class TokenStore
    {
        private readonly string _folderPath;
        private readonly string _filePath;

        public TokenStore()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var env = Environment.GetEnvironmentVariable("ENVIRONMENT");
            var folderName = string.Equals(env, "dev", StringComparison.OrdinalIgnoreCase) ? "recontrol_win_dev" : "recontrol_win";
            _folderPath = Path.Combine(appData, folderName);
            _filePath = Path.Combine(_folderPath, "tokens.dat");
        }

        /// <summary>
        /// Saves token data encrypted with DPAPI (CurrentUser scope).
        /// </summary>
        public void Save(TokenData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            Directory.CreateDirectory(_folderPath);

            var json = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);

            // Protect with DPAPI for current user
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(_filePath, protectedBytes);
        }

        /// <summary>
        /// Loads token data, or null if not present or unable to decrypt.
        /// </summary>
        public TokenData? Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return null;

                var protectedBytes = File.ReadAllBytes(_filePath);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(bytes);
                var data = JsonSerializer.Deserialize<TokenData>(json);
                return data;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes stored tokens.
        /// </summary>
        public void Clear()
        {
            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Convenience getters for individual values. Return null if no stored data.
        /// </summary>
        public string? GetAccessToken()
        {
            return Load()?.AccessToken;
        }

        public string? GetRefreshToken()
        {
            return Load()?.RefreshToken;
        }

        public string? GetDeviceId()
        {
            return Load()?.DeviceId;
        }

        public string? GetUserId()
        {
            return Load()?.UserId;
        }
    }
}
