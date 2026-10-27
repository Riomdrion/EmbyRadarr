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

        /// <summary>Provider id under which the badge is stored on the Emby item.</summary>
        public const string ProviderKey = "EmbyRadarrBadge";

        /// <summary>"Kind|percent|text".</summary>
        public string Serialize()
            => this.Kind + "|" + (this.Percent.HasValue ? this.Percent.Value.ToString(CultureInfo.InvariantCulture) : string.Empty) + "|" + this.Text;

        public static PosterBadge Parse(string value)
        {
            var parts = (value ?? string.Empty).Split(new[] { '|' }, 3);
            if (parts.Length != 3 || !Enum.TryParse(parts[0], out BadgeKind kind) || string.IsNullOrEmpty(parts[2]))
            {
                return null;
            }

            int? percent = int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : (int?)null;
            return new PosterBadge(kind, parts[2], percent);
        }

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
