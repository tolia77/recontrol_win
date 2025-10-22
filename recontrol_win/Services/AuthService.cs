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

        public AuthService()
        {
            var baseUrl = Environment.GetEnvironmentVariable("API_BASE_URL");
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("Environment variable 'API_BASE_URL' is not set.");

            _apiClient = new ApiClient(baseUrl);
        }

        /// <summary>
        /// Login with email, password and optional deviceId. Sends POST to /auth/login with JSON body.
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
                device_id = deviceId
            };

            // Use JsonContent to create application/json content
            HttpContent content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            return await _apiClient.PostAsync("/auth/login", content, false);
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

        public void Dispose()
        {
            _apiClient?.Dispose();
        }
    }
}
