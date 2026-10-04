using Denon.Core.Model;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Denon.Web.Remote.Services
{
    /// <summary>
    /// Server-side calls to the Denon Web API. (The page's JavaScript calls the API directly;
    /// this is only for what the page needs before it's rendered.)
    /// </summary>
    public interface IRemoteApiClient
    {
        /// <summary>Zones, volume limits and input sources, for building the page.</summary>
        /// <exception cref="RemoteApiException">The API couldn't be reached or returned an error.</exception>
        Task<ConfigurationItem> GetConfigurationAsync(CancellationToken cancellationToken = default);
    }


    /// <summary>
    /// Uses an HttpClient from HttpClientFactory (registered in Startup with the API's base
    /// address and a timeout), so connections are reused rather than opened for every page load.
    /// </summary>
    public class RemoteApiClient : IRemoteApiClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };

        private readonly HttpClient _http;

        public RemoteApiClient(HttpClient http)
        {
            _http = http;
        }

        public async Task<ConfigurationItem> GetConfigurationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using HttpResponseMessage response = await _http.GetAsync("GetConfiguration", cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    string title = await ReadProblemTitle(response, cancellationToken);
                    throw new RemoteApiException($"the API returned {(int)response.StatusCode}{(title is null ? "" : $": {title}")}");
                }

                return await response.Content.ReadFromJsonAsync<ConfigurationItem>(JsonOptions, cancellationToken) ?? new ConfigurationItem();
            }
            catch (HttpRequestException e)
            {
                throw new RemoteApiException(e.Message, e);
            }
            catch (TaskCanceledException e) when (!cancellationToken.IsCancellationRequested)
            {
                throw new RemoteApiException($"no answer within {_http.Timeout.TotalSeconds:0} seconds", e);
            }
            catch (JsonException e)
            {
                throw new RemoteApiException($"the API's answer wasn't valid JSON ({e.Message})", e);
            }
        }

        // The "title" of an ASP.NET Problem response, if the body is one.
        private static async Task<string> ReadProblemTitle(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                return doc.RootElement.TryGetProperty("title", out JsonElement title) ? title.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }


    public class RemoteApiException : Exception
    {
        public RemoteApiException(string message, Exception inner = null) : base(message, inner) { }
    }
}
