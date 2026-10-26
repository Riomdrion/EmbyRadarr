using System;
using EmbyRadarr.Configuration;

namespace EmbyRadarr.Radarr
{
    /// <summary>URL + API key pair. Separate from the saved config so "Test connection" can use unsaved form values.</summary>
    public class RadarrConnection
    {
        public RadarrConnection(string url, string apiKey)
        {
            this.BaseUrl = (url ?? string.Empty).Trim().TrimEnd('/');
            this.ApiKey = (apiKey ?? string.Empty).Trim();
        }

        public string BaseUrl { get; }

        public string ApiKey { get; }

        public static RadarrConnection FromConfig(PluginConfiguration config) => new RadarrConnection(config.RadarrUrl, config.ApiKey);

        public void Validate()
        {
            if (string.IsNullOrEmpty(this.BaseUrl) || string.IsNullOrEmpty(this.ApiKey))
            {
                throw new RadarrException("Radarr is not configured yet. Enter the Radarr URL and API key in the plugin settings.");
            }

            if (!Uri.TryCreate(this.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new RadarrException("The Radarr URL must start with http:// or https://, for example http://192.168.1.10:7878");
            }
        }
    }
}
