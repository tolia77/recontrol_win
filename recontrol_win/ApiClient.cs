using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace recontrol_win
{

    public class ApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public ApiClient(string baseUrl)
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl)
            };
        }

        /// <summary>
        /// Sends a GET request without custom headers.
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(string endpoint, bool enableRetryMiddleware = true)
        {
            return await GetAsync(endpoint, null, enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a GET request with optional headers.
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(string endpoint, IDictionary<string, string>? headers, bool enableRetryMiddleware = true)
        {
            return await SendAsync(() => CreateRequestMessage(HttpMethod.Get, endpoint, null, headers), enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a POST request without custom headers.
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string endpoint, HttpContent content, bool enableRetryMiddleware = true)
        {
            return await PostAsync(endpoint, content, null, enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a POST request with optional headers.
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string endpoint, HttpContent content, IDictionary<string, string>? headers, bool enableRetryMiddleware = true)
        {
            return await SendAsync(() => CreateRequestMessage(HttpMethod.Post, endpoint, content, headers), enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a PUT request without custom headers.
        /// </summary>
        public async Task<HttpResponseMessage> PutAsync(string endpoint, HttpContent content, bool enableRetryMiddleware = true)
        {
            return await PutAsync(endpoint, content, null, enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a PUT request with optional headers.
        /// </summary>
        public async Task<HttpResponseMessage> PutAsync(string endpoint, HttpContent content, IDictionary<string, string>? headers, bool enableRetryMiddleware = true)
        {
            return await SendAsync(() => CreateRequestMessage(HttpMethod.Put, endpoint, content, headers), enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a DELETE request without custom headers.
        /// </summary>
        public async Task<HttpResponseMessage> DeleteAsync(string endpoint, bool enableRetryMiddleware = true)
        {
            return await DeleteAsync(endpoint, null, enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a DELETE request with optional headers.
        /// </summary>
        public async Task<HttpResponseMessage> DeleteAsync(string endpoint, IDictionary<string, string>? headers, bool enableRetryMiddleware = true)
        {
            return await SendAsync(() => CreateRequestMessage(HttpMethod.Delete, endpoint, null, headers), enableRetryMiddleware);
        }

        /// <summary>
        /// Helper to create a new HttpRequestMessage for each attempt (so it can be retried safely).
        /// </summary>
        private HttpRequestMessage CreateRequestMessage(HttpMethod method, string endpoint, HttpContent? content = null, IDictionary<string, string>? headers = null)
        {
            var request = new HttpRequestMessage(method, endpoint);
            if (content != null)
                request.Content = content;

            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    // Try to add header; if it's a content header, add to content instead
                    if (!request.Headers.TryAddWithoutValidation(kv.Key, kv.Value) && request.Content != null)
                    {
                        request.Content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }
            }

            return request;
        }

        /// <summary>
        /// Core logic for sending requests and retrying on 401. Accepts a request factory to create a fresh HttpRequestMessage per attempt.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, bool enableRetryMiddleware)
        {
            HttpRequestMessage request = requestFactory();
            HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (!enableRetryMiddleware)
                return response;

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Console.WriteLine("[ApiClient] Received 401 Unauthorized. Retrying request...");

                // Dispose old response before retrying
                response.Dispose();

                // Create a new request for the retry
                request = requestFactory();
                response = await _httpClient.SendAsync(request);
            }

            return response;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

}
