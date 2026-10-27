using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmbyRadarr.Radarr;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace EmbyRadarr.Channel
{
    /// <summary>
    /// Turns Radarr data into channel items. Pure functions, no I/O.
    /// All text is Dutch and never mentions Radarr: users only see "Films aanvragen".
    /// </summary>
    public static class ItemBuilder
    {
        public const string DiscoverFolderId = "discover";
        public const string TrendingFolderId = "discover-trending";
        public const string PopularFolderId = "discover-popular";
        public const string RecommendedFolderId = "discover-recommended";

        /// <summary>Search result folders are "discover-search-&lt;n&gt;" so they share Discover's favorite handling.</summary>
        public const string SearchFolderPrefix = "discover-search-";
        public const string SearchRootFolderId = "search";
        public const string WantedFolderId = "wanted";
        public const string UpcomingFolderId = "upcoming";
        public const string QueueFolderId = "queue";

        /// <summary>
        /// Provider id that marks items a favorite (heart) acts on: "request:&lt;tmdbId&gt;" or "search:&lt;radarrId&gt;".
        /// </summary>
        public const string ActionKey = "EmbyRadarrAction";

        // Own month names: the server may run without Dutch culture data.
        private static readonly string[] Months = { "januari", "februari", "maart", "april", "mei", "juni", "juli", "augustus", "september", "oktober", "november", "december" };

        public static IEnumerable<Movie> SelectWanted(IEnumerable<Movie> movies)
            => movies.Where(m => m.Monitored && !m.HasFile);

        public static ChannelItemInfo Wanted(Movie m)
        {
            var status = m.IsAvailable
                ? "Aangevraagd, maar nog niet te kijken. Tik op het hartje om opnieuw te laten zoeken."
                : "Aangevraagd. De film is nog niet uitgekomen.";
            var item = Base(m, WantedFolderId + ":" + m.Id, "Nog niet te kijken", status);
            item.PremiereDate = FirstRelease(m);
            item.Tags = new List<string> { "Aangevraagd" };
            if (m.IsAvailable && m.Id > 0)
            {
                item.ProviderIds[ActionKey] = "search:" + m.Id.ToString(CultureInfo.InvariantCulture);
            }

            return item;
        }

        public static ChannelItemInfo Upcoming(Movie m, DateTime todayUtc)
        {
            var next = NextRelease(m, todayUtc);
            string status;
            string shortStatus;
            if (next == null)
            {
                status = "Komt binnenkort uit.";
                shortStatus = "Binnenkort";
            }
            else
            {
                var d = next.Value.Date.UtcDateTime;
                status = "Komt uit op " + LongDate(d) + " (" + next.Value.Kind + ").";
                shortStatus = "Verwacht " + d.Day + " " + Months[d.Month - 1].Substring(0, 3);
            }

            if (m.HasFile)
            {
                status += " Staat al in je bibliotheek.";
                shortStatus = "Al te kijken";
            }
            else
            {
                status += " Nog niet te kijken.";
            }

            var item = Base(m, UpcomingFolderId + ":" + m.Id, shortStatus, status);
            item.PremiereDate = next?.Date ?? FirstRelease(m);
            item.Tags = new List<string> { "Binnenkort" };
            return item;
        }

        public static ChannelItemInfo Queued(QueueItem q)
        {
            var movie = q.Movie ?? new Movie { Id = q.MovieId, Title = q.Title };
            var status = QueueStatus(q);
            var item = Base(movie, QueueFolderId + ":" + q.Id, status, status + ". Nog niet te kijken.");
            item.PremiereDate = FirstRelease(movie);
            item.Tags = new List<string> { "Wordt binnengehaald" };
            return item;
        }

        /// <summary>A Discover or search result. <paramref name="inRadarr"/> is the matching requested movie, if any.</summary>
        public static ChannelItemInfo Discover(Movie m, Movie inRadarr, string folderId)
        {
            ChannelItemInfo item;
            if (inRadarr?.HasFile == true)
            {
                item = Base(m, folderId + ":" + m.TmdbId, "Al te kijken", "Staat al in je bibliotheek. Je kunt hem kijken via Films.");
            }
            else if (inRadarr != null)
            {
                item = Base(m, folderId + ":" + m.TmdbId, "Aangevraagd", "Is al aangevraagd, maar nog niet te kijken.");
            }
            else
            {
                item = Base(m, folderId + ":" + m.TmdbId, "Tik ♥ om aan te vragen", "Nog niet beschikbaar. Tik op het hartje (favoriet) om deze film aan te vragen.");
                item.ProviderIds[ActionKey] = "request:" + m.TmdbId.ToString(CultureInfo.InvariantCulture);
            }

            item.PremiereDate = FirstRelease(m);
            item.Tags = new List<string> { "Ontdekken" };
            return item;
        }

        /// <summary>E.g. "Wordt gedownload 45%, nog 12 min", "Gepauzeerd 10%", "Bijna klaar", "Mislukt".</summary>
        public static string QueueStatus(QueueItem q)
        {
            // (size - left) * 100 / size avoids 1 - 0.55 = 0.4499... rounding down to 44%.
            var percent = q.Size > 0 ? (int)Math.Floor(((q.Size - q.SizeLeft) * 100 / q.Size) + 1e-9) : 0;
            percent = Math.Max(0, Math.Min(100, percent));

            switch (q.TrackedDownloadState)
            {
                case "importPending":
                case "importing":
                    return "Bijna klaar";
                case "importBlocked":
                    return "Bijna klaar, wacht op de beheerder";
                case "failedPending":
                case "failed":
                    return "Mislukt";
            }

            switch (q.Status)
            {
                case "queued":
                case "delay":
                    return "In de wachtrij";
                case "paused":
                    return "Gepauzeerd " + percent + "%";
                case "completed":
                    return "Bijna klaar";
                case "failed":
                    return "Mislukt";
                case "downloadClientUnavailable":
                    return "Even niet bereikbaar";
                default:
                    var left = FormatTimeLeft(q.TimeLeft);
                    return "Wordt gedownload " + percent + "%" + (left == null ? string.Empty : ", nog " + left);
            }
        }

        /// <summary>The earliest cinema/digital/physical release on or after today.</summary>
        public static (DateTimeOffset Date, string Kind)? NextRelease(Movie m, DateTime todayUtc)
        {
            return Releases(m)
                .Where(r => r.Date.UtcDateTime.Date >= todayUtc.Date)
                .OrderBy(r => r.Date)
                .Select(r => ((DateTimeOffset Date, string Kind)?)r)
                .FirstOrDefault();
        }

        /// <summary>Applies Emby's requested sort (if any) or the folder's default, then paging.</summary>
        public static ChannelItemResult Page(List<ChannelItemInfo> items, InternalChannelItemQuery query, string folderId)
        {
            IEnumerable<ChannelItemInfo> sorted;
            switch (query.SortBy)
            {
                case ChannelItemSortField.Name:
                    sorted = items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
                    break;
                case ChannelItemSortField.DateCreated:
                    sorted = items.OrderBy(i => i.DateCreated ?? DateTimeOffset.MaxValue);
                    break;
                case ChannelItemSortField.Runtime:
                    sorted = items.OrderBy(i => i.RunTimeTicks ?? 0);
                    break;
                case ChannelItemSortField.PremiereDate:
                    sorted = items.OrderBy(i => i.PremiereDate ?? DateTimeOffset.MaxValue);
                    break;
                default:
                    // Queue, Discover and search results: keep Radarr's order. Requested/Upcoming: soonest release first.
                    var keepOrder = folderId == QueueFolderId || folderId.StartsWith(DiscoverFolderId, StringComparison.Ordinal);
                    sorted = keepOrder ? items : items.OrderBy(i => i.PremiereDate ?? DateTimeOffset.MaxValue);
                    break;
            }

            if (query.SortBy.HasValue && query.SortDescending)
            {
                sorted = sorted.Reverse();
            }

            var page = sorted.Skip(query.StartIndex ?? 0);
            if (query.Limit.HasValue)
            {
                page = page.Take(query.Limit.Value);
            }

            return new ChannelItemResult { Items = page.ToList(), TotalRecordCount = items.Count };
        }

        /// <summary>A non-playable folder whose name and description explain a problem or an empty list.</summary>
        public static ChannelItemInfo Message(string id, string title, string text)
            => new ChannelItemInfo
            {
                Id = "msg:" + id,
                Name = title,
                Overview = text,
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Container,
            };

        public static string PosterUrl(Movie m)
        {
            if (!string.IsNullOrEmpty(m.RemotePoster))
            {
                return m.RemotePoster;
            }

            // Only the remote (TMDb) URL: Radarr's own /MediaCover URLs would need the API key.
            return m.Images?.FirstOrDefault(i => i.CoverType == "poster" && !string.IsNullOrEmpty(i.RemoteUrl))?.RemoteUrl;
        }

        /// <summary>"14 november 2026".</summary>
        public static string LongDate(DateTime d) => d.Day + " " + Months[d.Month - 1] + " " + d.Year;

        private static ChannelItemInfo Base(Movie m, string id, string shortStatus, string status)
        {
            var overview = status;
            if (!string.IsNullOrWhiteSpace(m.Overview))
            {
                overview += "\n\n" + m.Overview.Trim();
            }

            var providerIds = new ProviderIdDictionary();
            if (m.TmdbId > 0)
            {
                providerIds[MetadataProviders.Tmdb.ToString()] = m.TmdbId.ToString(CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(m.ImdbId))
            {
                providerIds[MetadataProviders.Imdb.ToString()] = m.ImdbId;
            }

            if (!string.IsNullOrEmpty(m.YouTubeTrailerId))
            {
                providerIds[TrailerExternalId.ProviderKey] = m.YouTubeTrailerId;
                overview += "\n\nTrailer: " + TrailerExternalId.UrlFor(m.YouTubeTrailerId);
            }

            return new ChannelItemInfo
            {
                Id = id,
                // The status is part of the name so it shows in every Emby app, even in poster grids.
                Name = (string.IsNullOrEmpty(m.Title) ? "Onbekende film" : m.Title) + " · " + shortStatus,
                ProductionYear = m.Year > 0 ? m.Year : (int?)null,
                Overview = overview,
                Tagline = status,
                ImageUrl = PosterUrl(m),
                Type = ChannelItemType.Media,
                ContentType = ChannelMediaContentType.Movie,
                MediaType = ChannelMediaType.Video,
                MediaSources = new List<MediaSourceInfo>(),
                ProviderIds = providerIds,
                Genres = m.Genres ?? new List<string>(),
                Studios = string.IsNullOrEmpty(m.Studio) ? new List<string>() : new List<string> { m.Studio },
                OfficialRating = m.Certification,
                RunTimeTicks = m.Runtime > 0 ? TimeSpan.FromMinutes(m.Runtime).Ticks : (long?)null,
                DateCreated = ParseDate(m.Added),
            };
        }

        private static IEnumerable<(DateTimeOffset Date, string Kind)> Releases(Movie m)
        {
            var list = new List<(DateTimeOffset, string)>();
            Add(list, m.InCinemas, "in de bioscoop");
            Add(list, m.DigitalRelease, "digitaal");
            Add(list, m.PhysicalRelease, "op dvd/blu-ray");
            return list;
        }

        private static DateTimeOffset? FirstRelease(Movie m)
            => Releases(m).Select(r => (DateTimeOffset?)r.Date).OrderBy(d => d).FirstOrDefault();

        private static void Add(List<(DateTimeOffset, string)> list, string value, string kind)
        {
            var date = ParseDate(value);
            if (date.HasValue)
            {
                list.Add((date.Value, kind));
            }
        }

        internal static DateTimeOffset? ParseDate(string value)
            => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTimeOffset?)null;

        internal static string FormatTimeLeft(string value)
        {
            if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var t) || t <= TimeSpan.Zero)
            {
                return null;
            }

            if (t.TotalHours >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} u {1} min", (int)t.TotalHours, t.Minutes);
            }

            return Math.Max(1, (int)Math.Round(t.TotalMinutes)) + " min";
        }
    }
}
