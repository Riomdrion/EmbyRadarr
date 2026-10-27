using System.Globalization;

namespace EmbyRadarr.Posters
{
    /// <summary>Must match EmbyPosterBanners' BannerKind names.</summary>
    public enum BadgeKind
    {
        /// <summary>Not requested yet: tap the heart.</summary>
        Request,
        Requested,
        Upcoming,
        Downloading,
        Available,
        Failed,
    }

    /// <summary>
    /// Status for the separate "Poster Banners" plugin, stored on the channel item as provider id
    /// "PosterBanner" = "Kind|percent|text" (e.g. "Downloading|45|DOWNLOADEN 45%"). This plugin draws nothing itself.
    /// </summary>
    public class PosterBadge
    {
        public const string ProviderKey = "PosterBanner";

        public PosterBadge(BadgeKind kind, string text, int? percent = null)
        {
            this.Kind = kind;
            this.Text = text;
            this.Percent = percent;
        }

        public BadgeKind Kind { get; }

        public string Text { get; }

        public int? Percent { get; }

        public string Serialize()
            => this.Kind + "|" + (this.Percent.HasValue ? this.Percent.Value.ToString(CultureInfo.InvariantCulture) : string.Empty) + "|" + this.Text;
    }
}
