using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EmbyRadarr.Radarr;
using MediaBrowser.Model.Logging;

namespace EmbyRadarr.Update
{
    /// <summary>
    /// Self-update from this plugin's GitHub releases: download EmbyRadarr.dll from the latest release
    /// and swap it in next to the running copy. Emby picks it up on the next restart.
    /// Uses plain .NET HttpClient (not Emby's IHttpClient) so redirects to GitHub's download host
    /// drop the Authorization header, as GitHub requires.
    /// </summary>
    public class PluginUpdater
    {
        public const string Repository = "Riomdrion/EmbyRadarr";
        public const string AssetName = "EmbyRadarr.dll";

        private readonly HttpClient http;
        private readonly ILogger logger;

        public PluginUpdater(ILogger logger, HttpMessageHandler handler = null)
        {
            this.logger = logger;
            this.http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(60) };
            this.http.DefaultRequestHeaders.UserAgent.ParseAdd("EmbyRadarr-Plugin");
        }

        public async Task<UpdateInfo> CheckLatest(Version current, string token, CancellationToken cancellationToken)
        {
            var url = "https://api.github.com/repos/" + Repository + "/releases/latest";
            using (var request = Request(url, "application/vnd.github+json", token))
            using (var response = await this.Send(request, cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new RadarrException(string.IsNullOrEmpty(token)
                        ? "No release found. If the GitHub repository is private, enter a GitHub token."
                        : "No release found, or the token has no access to " + Repository + ".");
                }

                EnsureSuccess(response, "checking for updates");
                var release = Read<GitHubRelease>(await response.Content.ReadAsStreamAsync().ConfigureAwait(false));

                var asset = release.Assets?.FirstOrDefault(a => string.Equals(a.Name, AssetName, StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                {
                    throw new RadarrException("Release " + release.TagName + " has no " + AssetName + " attached.");
                }

                var latest = ParseVersion(release.TagName);
                return new UpdateInfo
                {
                    Tag = release.TagName,
                    Latest = latest,
                    Asset = asset,
                    IsNewer = latest != null && latest > Normalize(current),
                };
            }
        }

        /// <summary>Downloads the asset, checks it, and replaces <paramref name="targetPath"/>. Keeps a .bak copy.</summary>
        public async Task Install(UpdateInfo update, string token, string targetPath, CancellationToken cancellationToken)
        {
            // Private repos need the API asset URL + token; public ones can use the plain download URL.
            var url = string.IsNullOrEmpty(token) ? update.Asset.BrowserDownloadUrl : update.Asset.ApiUrl;
            byte[] bytes;
            using (var request = Request(url, "application/octet-stream", token))
            using (var response = await this.Send(request, cancellationToken).ConfigureAwait(false))
            {
                EnsureSuccess(response, "downloading the update");
                bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }

            Verify(bytes, update.Asset);

            var temp = targetPath + ".download";
            File.WriteAllBytes(temp, bytes);
            try
            {
                // Rename-based swap: on Linux the running process keeps its already-loaded copy.
                File.Replace(temp, targetPath, targetPath + ".bak");
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("Could not replace {0}", ex, targetPath);
                throw new RadarrException("Downloaded the update but could not replace the plugin file (" + ex.Message + "). "
                    + "The new file is at " + temp + "; stop Emby and rename it to " + Path.GetFileName(targetPath) + ".");
            }

            this.logger.Info("Plugin updated to {0} ({1} bytes)", update.Tag, bytes.Length);
        }

        internal static Version ParseVersion(string tag)
            => Version.TryParse((tag ?? string.Empty).TrimStart('v', 'V'), out var v) ? Normalize(v) : null;

        private static Version Normalize(Version v)
            => v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));

        private static void Verify(byte[] bytes, ReleaseAsset asset)
        {
            if (bytes.Length < 2 || bytes[0] != 'M' || bytes[1] != 'Z')
            {
                throw new RadarrException("The downloaded file is not a DLL. Nothing was changed.");
            }

            if (asset.Size > 0 && bytes.Length != asset.Size)
            {
                throw new RadarrException("The download is incomplete. Nothing was changed.");
            }

            // GitHub publishes "sha256:<hex>" digests for release assets.
            if (asset.Digest != null && asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                using (var sha = SHA256.Create())
                {
                    var actual = string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
                    if (!string.Equals(actual, asset.Digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new RadarrException("The download's checksum does not match GitHub's. Nothing was changed.");
                    }
                }
            }
        }

        private static HttpRequestMessage Request(string url, string accept, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd(accept);
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return request;
        }

        private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await this.http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                this.logger.Warn("GitHub request failed: {0}", ex.Message);
                throw new RadarrException("Could not reach GitHub (" + ex.Message + ").", ex);
            }
        }

        private static void EnsureSuccess(HttpResponseMessage response, string action)
        {
            var code = (int)response.StatusCode;
            if (code == 401 || code == 403)
            {
                throw new RadarrException("GitHub refused access while " + action + " (HTTP " + code + "). Check the GitHub token.");
            }

            if (code < 200 || code >= 300)
            {
                throw new RadarrException("GitHub returned HTTP " + code + " while " + action + ".");
            }
        }

        private static T Read<T>(Stream stream)
        {
            try
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            catch (Exception ex)
            {
                throw new RadarrException("Unexpected response from GitHub.", ex);
            }
        }
    }

    public class UpdateInfo
    {
        public string Tag { get; set; }

        public Version Latest { get; set; }

        public bool IsNewer { get; set; }

        public ReleaseAsset Asset { get; set; }
    }

    [DataContract]
    public class GitHubRelease
    {
        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "assets")]
        public List<ReleaseAsset> Assets { get; set; }
    }

    [DataContract]
    public class ReleaseAsset
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>API URL; with Accept: application/octet-stream it downloads the file (works for private repos).</summary>
        [DataMember(Name = "url")]
        public string ApiUrl { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }

        [DataMember(Name = "digest")]
        public string Digest { get; set; }
    }
}
