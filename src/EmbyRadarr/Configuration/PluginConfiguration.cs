using MediaBrowser.Model.Plugins;

namespace EmbyRadarr.Configuration
{
    /// <summary>Persisted settings (written by Emby to plugins/configurations/EmbyRadarr.xml).</summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string RadarrUrl { get; set; } = string.Empty;

        /// <summary>Never logged and never sent back to the browser.</summary>
        public string ApiKey { get; set; } = string.Empty;

        public int QualityProfileId { get; set; }

        public string RootFolderPath { get; set; } = string.Empty;

        /// <summary>How long Radarr responses are cached (1-5 minutes).</summary>
        public int CacheMinutes { get; set; } = 2;

        /// <summary>Optional, read-only GitHub token for updating from a private repository. Never logged.</summary>
        public string GitHubToken { get; set; } = string.Empty;

        public int CacheMinutesClamped => System.Math.Max(1, System.Math.Min(5, this.CacheMinutes));

        public bool IsConfigured => !string.IsNullOrWhiteSpace(this.RadarrUrl) && !string.IsNullOrWhiteSpace(this.ApiKey);
    }
}
