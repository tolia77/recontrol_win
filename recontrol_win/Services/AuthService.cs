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

            _apiClient = new ApiClient(baseUrl);
        }

        /// <summary>
        /// Login with email, password and optional deviceId. Sends POST to /auth/login with JSON body.
        /// On success, stores tokens via DPAPI in user AppData.
        /// Returns the HttpResponseMessage for callers to handle (e.g. read tokens or errors).
        /// </summary>
        public async Task<HttpResponseMessage> LoginAsync(string email, string password, string? deviceId = null)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentNullException(nameof(email));
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentNullException(nameof(password));

            var payload = new
            {
                email,
                password,
                device_id = deviceId,
                device_name = Environment.MachineName,
                client_type = "desktop"
            };

            // Use JsonContent to create application/json content
            HttpContent content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            var response = await _apiClient.PostAsync("/auth/login", content, false);

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var userId = root.GetProperty("user_id").GetString() ?? string.Empty;
                    var devId = root.GetProperty("device_id").GetString() ?? string.Empty;
                    var access = root.GetProperty("access_token").GetString() ?? string.Empty;
                    var refresh = root.GetProperty("refresh_token").GetString() ?? string.Empty;

                    var tokenData = new TokenData(userId, devId, access, refresh);
                    _tokenStore.Save(tokenData);
                }
                catch
                {
                    // ignore save errors but keep response for caller
                }
            }

            return response;
        }

        /// <summary>
        /// Refresh tokens by sending a request with a Refresh-Token header. Sends POST to /auth/refresh.
        /// </summary>
        public async Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) throw new ArgumentNullException(nameof(refreshToken));

            var headers = new Dictionary<string, string>
            {
                { "Refresh-Token", refreshToken }
            };

            // No body required for refresh; send an empty POST
            HttpContent emptyContent = new StringContent(string.Empty);

            return await _apiClient.PostAsync("/auth/refresh", emptyContent, headers);
        }

        // Convenience getters that read stored tokens
        public string? GetAccessToken() => _tokenStore.GetAccessToken();
        public string? GetRefreshToken() => _tokenStore.GetRefreshToken();
        public string? GetDeviceId() => _tokenStore.GetDeviceId();
        public string? GetUserId() => _tokenStore.GetUserId();
        public TokenData? GetTokenData() => _tokenStore.Load();

        public void Dispose()
        {
            _apiClient?.Dispose();
        }
    }
}
