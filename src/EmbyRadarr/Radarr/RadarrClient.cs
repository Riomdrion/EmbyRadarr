using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace EmbyRadarr.Radarr
{
    /// <summary>
    /// Minimal Radarr API v3 client. Uses plain .NET HttpClient (not Emby's IHttpClient) so GET and POST
    /// behave the same everywhere and can be unit tested with a fake handler.
    /// </summary>
    public class RadarrClient
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private readonly HttpClient http;
        private readonly ILogger logger;

        public RadarrClient(ILogger logger, HttpMessageHandler handler = null)
        {
            this.logger = logger;
            this.http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = Timeout };
        }

        public Task<SystemStatus> GetSystemStatus(RadarrConnection conn, CancellationToken token)
            => this.Send<SystemStatus>(conn, HttpMethod.Get, "/api/v3/system/status", null, token);

        public Task<List<QualityProfile>> GetQualityProfiles(RadarrConnection conn, CancellationToken token)
            => this.Send<List<QualityProfile>>(conn, HttpMethod.Get, "/api/v3/qualityprofile", null, token);

        public Task<List<RootFolder>> GetRootFolders(RadarrConnection conn, CancellationToken token)
            => this.Send<List<RootFolder>>(conn, HttpMethod.Get, "/api/v3/rootfolder", null, token);

        public Task<List<Movie>> GetMovies(RadarrConnection conn, CancellationToken token)
            => this.Send<List<Movie>>(conn, HttpMethod.Get, "/api/v3/movie", null, token);

        /// <summary>Monitored movies with a cinema, digital or physical release between start and end.</summary>
        public Task<List<Movie>> GetCalendar(RadarrConnection conn, DateTime startUtc, DateTime endUtc, CancellationToken token)
            => this.Send<List<Movie>>(
                conn,
                HttpMethod.Get,
                "/api/v3/calendar?unmonitored=false&start=" + startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                + "&end=" + endUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                null,
                token);

        public Task<QueuePage> GetQueue(RadarrConnection conn, CancellationToken token)
            => this.Send<QueuePage>(conn, HttpMethod.Get, "/api/v3/queue?page=1&pageSize=200&includeMovie=true", null, token);

        /// <summary>Radarr's "Discover" list: trending, popular and recommended movies.</summary>
        public Task<List<Movie>> GetDiscover(RadarrConnection conn, CancellationToken token)
            => this.Send<List<Movie>>(conn, HttpMethod.Get, "/api/v3/importlist/movie?includeRecommendations=true&includeTrending=true&includePopular=true", null, token);

        public Task<Movie> LookupTmdb(RadarrConnection conn, int tmdbId, CancellationToken token)
            => this.Send<Movie>(conn, HttpMethod.Get, "/api/v3/movie/lookup/tmdb?tmdbId=" + tmdbId.ToString(CultureInfo.InvariantCulture), null, token);

        public Task<Movie> AddMovie(RadarrConnection conn, AddMovieRequest request, CancellationToken token)
            => this.Send<Movie>(conn, HttpMethod.Post, "/api/v3/movie", request, token);

        public Task<CommandResult> SendCommand(RadarrConnection conn, CommandRequest command, CancellationToken token)
            => this.Send<CommandResult>(conn, HttpMethod.Post, "/api/v3/command", command, token);

        private async Task<T> Send<T>(RadarrConnection conn, HttpMethod method, string path, object body, CancellationToken token)
        {
            conn.Validate();

            using (var request = new HttpRequestMessage(method, conn.BaseUrl + path))
            {
                // The key travels in a header, so it never appears in a URL or log line.
                request.Headers.Add("X-Api-Key", conn.ApiKey);
                request.Headers.Accept.ParseAdd("application/json");
                if (body != null)
                {
                    request.Content = new StringContent(Serialize(body), Encoding.UTF8, "application/json");
                }

                HttpResponseMessage response;
                try
                {
                    response = await this.http.SendAsync(request, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Connection refused, DNS failure, timeout, TLS errors, ...
                    this.logger.Warn("Radarr request {0} failed: {1}", path, ex.Message);
                    throw new RadarrException("Could not reach Radarr at " + conn.BaseUrl + ". Is it running and is the URL correct? (" + ex.Message + ")", ex);
                }

                using (response)
                {
                    var content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    this.EnsureSuccess(response.StatusCode, path, content);
                    return this.Deserialize<T>(content, path);
                }
            }
        }

        private void EnsureSuccess(HttpStatusCode status, string path, byte[] content)
        {
            var code = (int)status;
            if (code >= 200 && code < 300)
            {
                return;
            }

            this.logger.Warn("Radarr request {0} returned HTTP {1}", path, code);

            switch (status)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new RadarrException("Radarr rejected the API key (HTTP " + code + "). Check Settings > General > API Key in Radarr.");
                case HttpStatusCode.NotFound:
                    throw new RadarrException("Radarr API not found (HTTP 404). Check the URL, including any URL base such as /radarr.");
                case HttpStatusCode.BadRequest:
                    throw new RadarrException("Radarr refused the request: " + ValidationMessage(content));
                default:
                    throw new RadarrException("Radarr returned HTTP " + code + ".");
            }
        }

        /// <summary>Radarr answers 400 with [{"propertyName":..,"errorMessage":..}].</summary>
        private static string ValidationMessage(byte[] content)
        {
            try
            {
                var errors = (List<ValidationError>)new DataContractJsonSerializer(typeof(List<ValidationError>)).ReadObject(new MemoryStream(content));
                var messages = errors?.Select(e => e.ErrorMessage).Where(m => !string.IsNullOrEmpty(m)).ToList();
                if (messages != null && messages.Count > 0)
                {
                    return string.Join("; ", messages);
                }
            }
            catch (Exception)
            {
                // Not the usual validation format; fall through.
            }

            return "HTTP 400";
        }

        private T Deserialize<T>(byte[] content, string path)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                return (T)serializer.ReadObject(new MemoryStream(content));
            }
            catch (Exception ex)
            {
                this.logger.Warn("Unexpected response from Radarr for {0}: {1}", path, ex.Message);
                throw new RadarrException("Radarr sent a response the plugin could not read. Is the URL really pointing at Radarr?", ex);
            }
        }

        internal static string Serialize(object body)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(body.GetType()).WriteObject(stream, body);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        [DataContract]
        private class ValidationError
        {
            [DataMember(Name = "errorMessage")]
            public string ErrorMessage { get; set; }
        }
    }
}
