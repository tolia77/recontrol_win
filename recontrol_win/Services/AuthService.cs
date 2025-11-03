using recontrol_win.Tools;
using recontrol_win.Internal;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace recontrol_win.Services
{
    internal class AuthService : IDisposable
    {
        private readonly ApiClient _apiClient;
        private readonly TokenStore _tokenStore = new TokenStore();

        public AuthService()
        {
            var baseUrl = Environment.GetEnvironmentVariable("API_BASE_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("Environment variable 'API_BASE_URL' is not set.");

            _apiClient = new ApiClient(
                baseUrl,
                getAccessToken: GetAccessToken,
                refreshTokens: RefreshTokensAsync
            );
        }

        public async Task<HttpResponseMessage> LoginAsync(string email, string password, string? deviceId = null)
        {
            InternalLogger.Log($"AuthService.LoginAsync called: email={email}, deviceId={deviceId}");
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentNullException(nameof(email));
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentNullException(nameof(password));

            var storedDeviceId = _tokenStore.GetDeviceId();
            var effectiveDeviceId = deviceId ?? storedDeviceId;

            object payload = !string.IsNullOrWhiteSpace(effectiveDeviceId)
                ? new { email, password, device_id = effectiveDeviceId, client_type = "desktop" }
                : new { email, password, device_name = Environment.MachineName, client_type = "desktop" };

            HttpContent content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var response = await _apiClient.PostAsync("/auth/login", content);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var userId = root.GetProperty("user_id").GetString() ?? string.Empty;
                var devId = root.GetProperty("device_id").GetString() ?? string.Empty;
                var access = root.GetProperty("access_token").GetString() ?? string.Empty;
                var refresh = root.GetProperty("refresh_token").GetString() ?? string.Empty;

                _tokenStore.Save(new TokenData(userId, devId, access, refresh));
                InternalLogger.Log($"AuthService.LoginAsync success: userId={userId}, deviceId={devId}");
            }
            else
            {
                InternalLogger.Log($"AuthService.LoginAsync failed: status={response.StatusCode}");
            }

            return response;
        }

        public async Task<bool> RefreshTokensAsync()
        {
            InternalLogger.Log("AuthService.RefreshTokensAsync called");
            var refreshToken = GetRefreshToken();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                InternalLogger.Log("AuthService.RefreshTokensAsync: no refresh token");
                return false;
            }

            var headers = new Dictionary<string, string> { { "Refresh-Token", refreshToken } };
            HttpContent empty = new StringContent(string.Empty);

            var response = await _apiClient.PostAsync("/auth/refresh", empty, headers);
            if (!response.IsSuccessStatusCode)
            {
                InternalLogger.Log($"AuthService.RefreshTokensAsync failed: status={response.StatusCode}");
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var newAccess = root.GetProperty("access_token").GetString();
            var newRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : refreshToken;

            if (string.IsNullOrWhiteSpace(newAccess))
            {
                InternalLogger.Log("AuthService.RefreshTokensAsync: no new access token");
                return false;
            }

            var current = _tokenStore.Load();
            if (current != null)
            {
                var updated = new TokenData(current.UserId, current.DeviceId, newAccess, newRefresh ?? current.RefreshToken);
                _tokenStore.Save(updated);
            }

            InternalLogger.Log("AuthService.RefreshTokensAsync succeeded");
            return true;
        }

        public string? GetAccessToken() => _tokenStore.GetAccessToken();
        public string? GetRefreshToken() => _tokenStore.GetRefreshToken();
        public string? GetDeviceId() => _tokenStore.GetDeviceId();
        public string? GetUserId() => _tokenStore.GetUserId();
        public TokenData? GetTokenData() => _tokenStore.Load();

        public void Dispose() => _apiClient.Dispose();
    }
}
