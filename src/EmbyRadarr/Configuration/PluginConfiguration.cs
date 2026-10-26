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

        public bool IsConfigured => !string.IsNullOrWhiteSpace(this.RadarrUrl) && !string.IsNullOrWhiteSpace(this.ApiKey);
    }
}
