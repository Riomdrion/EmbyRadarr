using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace EmbyRadarr.Channel
{
    /// <summary>
    /// Registers "YouTube trailer" as an external id, so Emby shows a clickable trailer link
    /// on items that carry the id (only this plugin's channel items do).
    /// </summary>
    public class TrailerExternalId : IExternalId
    {
        public const string ProviderKey = "RadarrYouTubeTrailer";

        public string Name => "YouTube-trailer";

        public string Key => ProviderKey;

        public string UrlFormatString => "https://www.youtube.com/watch?v={0}";

        public static string UrlFor(string youTubeId) => "https://www.youtube.com/watch?v=" + youTubeId;

        public bool Supports(IHasProviderIds item)
            => item?.ProviderIds != null && item.ProviderIds.ContainsKey(ProviderKey);
    }
}
