using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;

namespace EmbyRadarr.Radarr
{
    /// <summary>Minimal Radarr API v3 client on top of Emby's IHttpClient.</summary>
    public class RadarrClient
    {
        private const int TimeoutMs = 15000;

        private readonly IHttpClient httpClient;
        private readonly ILogger logger;

        public RadarrClient(IHttpClient httpClient, ILogger logger)
        {
            this.httpClient = httpClient;
            this.logger = logger;
        }

        public Task<SystemStatus> GetSystemStatus(RadarrConnection conn, CancellationToken token)
            => this.Get<SystemStatus>(conn, "/api/v3/system/status", token);

        public Task<List<QualityProfile>> GetQualityProfiles(RadarrConnection conn, CancellationToken token)
            => this.Get<List<QualityProfile>>(conn, "/api/v3/qualityprofile", token);

        public Task<List<RootFolder>> GetRootFolders(RadarrConnection conn, CancellationToken token)
            => this.Get<List<RootFolder>>(conn, "/api/v3/rootfolder", token);

        private async Task<T> Get<T>(RadarrConnection conn, string path, CancellationToken token)
        {
            conn.Validate();

            var options = new HttpRequestOptions
            {
                Url = conn.BaseUrl + path,
                AcceptHeader = "application/json",
                CancellationToken = token,
                TimeoutMs = TimeoutMs,
                BufferContent = true,
                ThrowOnErrorResponse = false,
                // The key travels in a header, so it never appears in a logged URL. Keep Emby's own request logging off anyway.
                LogRequest = false,
                LogErrors = false,
                LogErrorResponseBody = false,
                LogResponse = false,
            };
            options.RequestHeaders["X-Api-Key"] = conn.ApiKey;

            try
            {
                using (var response = await this.httpClient.SendAsync(options, "GET").ConfigureAwait(false))
                {
                    EnsureSuccess(response.StatusCode, path);
                    return Deserialize<T>(response.Content, path);
                }
            }
            catch (RadarrException)
            {
                throw;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpException ex) when (ex.StatusCode.HasValue)
            {
                EnsureSuccess(ex.StatusCode.Value, path);
                throw;
            }
            catch (Exception ex)
            {
                // Connection refused, DNS failure, timeout, TLS errors, ...
                this.logger.Warn("Radarr request {0} failed: {1}", path, ex.Message);
                throw new RadarrException("Could not reach Radarr at " + conn.BaseUrl + ". Is it running and is the URL correct? (" + ex.Message + ")", ex);
            }
        }

        private void EnsureSuccess(HttpStatusCode status, string path)
        {
            if ((int)status >= 200 && (int)status < 300)
            {
                return;
            }

            this.logger.Warn("Radarr request {0} returned HTTP {1}", path, (int)status);

            switch (status)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new RadarrException("Radarr rejected the API key (HTTP " + (int)status + "). Check Settings > General > API Key in Radarr.");
                case HttpStatusCode.NotFound:
                    throw new RadarrException("Radarr API not found (HTTP 404). Check the URL, including any URL base such as /radarr.");
                default:
                    throw new RadarrException("Radarr returned HTTP " + (int)status + ".");
            }
        }

        private T Deserialize<T>(Stream stream, string path)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                return (T)serializer.ReadObject(stream);
            }
            catch (Exception ex)
            {
                this.logger.Warn("Unexpected response from Radarr for {0}: {1}", path, ex.Message);
                throw new RadarrException("Radarr sent a response the plugin could not read. Is the URL really pointing at Radarr?", ex);
            }
        }
    }
}
