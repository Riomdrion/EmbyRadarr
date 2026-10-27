using System;
using System.Collections.Concurrent;
using System.Globalization;

namespace EmbyRadarr.Posters
{
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

    /// <summary>What the poster banner shows for one channel item.</summary>
    public class PosterBadge
    {
        public PosterBadge(BadgeKind kind, string text, int? percent = null)
        {
            this.Kind = kind;
            this.Text = text;
            this.Percent = percent;
        }

        public BadgeKind Kind { get; }

        /// <summary>Banner text, e.g. "AANGEVRAAGD" or "DOWNLOADEN 45%".</summary>
        public string Text { get; }

        /// <summary>Download progress 0-100, only for <see cref="BadgeKind.Downloading"/>.</summary>
        public int? Percent { get; }

        /// <summary>Changes whenever the drawn poster would change; progress in steps of 5% to limit re-rendering.</summary>
        public string CacheKey
            => this.Kind + "|" + this.Text + "|" + (this.Percent.HasValue ? (this.Percent.Value / 5 * 5).ToString(CultureInfo.InvariantCulture) : "-");
    }

    /// <summary>Badges of the channel items built most recently, keyed by channel item id (Emby's ExternalId).</summary>
    public static class PosterBadges
    {
        private static readonly ConcurrentDictionary<string, PosterBadge> Badges = new ConcurrentDictionary<string, PosterBadge>(StringComparer.Ordinal);

        public static void Remember(string channelItemId, PosterBadge badge)
        {
            if (!string.IsNullOrEmpty(channelItemId) && badge != null)
            {
                Badges[channelItemId] = badge;
            }
        }

        public static PosterBadge Get(string channelItemId)
            => !string.IsNullOrEmpty(channelItemId) && Badges.TryGetValue(channelItemId, out var badge) ? badge : null;
    }
}
