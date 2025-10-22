using System;
using System.Net.Http;
using System.Threading.Tasks;
namespace recontrol_win
{

    public class ApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public ApiClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl)
            };
        }

        /// <summary>
        /// Sends a GET request.
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(string endpoint, bool enableRetryMiddleware = true)
        {
            return await SendAsync(() => _httpClient.GetAsync(endpoint), enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a POST request.
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string endpoint, HttpContent content, bool enableRetryMiddleware = true)
        {
            return await SendAsync(() => _httpClient.PostAsync(endpoint, content), enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a PUT request.
        /// </summary>
        public async Task<HttpResponseMessage> PutAsync(string endpoint, HttpContent content, bool enableRetryMiddleware = true)
        {
            return await SendAsync(() => _httpClient.PutAsync(endpoint, content), enableRetryMiddleware);
        }

        /// <summary>
        /// Sends a DELETE request.
        /// </summary>
        public async Task<HttpResponseMessage> DeleteAsync(string endpoint, bool enableRetryMiddleware = true)
        {
            return await SendAsync(() => _httpClient.DeleteAsync(endpoint), enableRetryMiddleware);
        }

        /// <summary>
        /// Core logic for sending requests and retrying on 401.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> requestFunc, bool enableRetryMiddleware)
        {
            HttpResponseMessage response = await requestFunc();

            if (!enableRetryMiddleware)
                return response;

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Console.WriteLine("[ApiClient] Received 401 Unauthorized. Retrying request...");

                // Dispose old response before retrying
                response.Dispose();

                response = await requestFunc();
            }

            return response;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

}
