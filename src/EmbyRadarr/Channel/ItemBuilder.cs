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
    /// <summary>Turns Radarr data into channel items. Pure functions, no I/O.</summary>
    public static class ItemBuilder
    {
        public const string DiscoverFolderId = "discover";
        public const string TrendingFolderId = "discover-trending";
        public const string PopularFolderId = "discover-popular";
        public const string RecommendedFolderId = "discover-recommended";
        public const string WantedFolderId = "wanted";
        public const string UpcomingFolderId = "upcoming";
        public const string QueueFolderId = "queue";

        /// <summary>
        /// Provider id that marks items a favorite (heart) acts on: "request:&lt;tmdbId&gt;" or "search:&lt;radarrId&gt;".
        /// </summary>
        public const string ActionKey = "EmbyRadarrAction";

        public static IEnumerable<Movie> SelectWanted(IEnumerable<Movie> movies)
            => movies.Where(m => m.Monitored && !m.HasFile);

        public static ChannelItemInfo Wanted(Movie m)
        {
            var status = m.IsAvailable
                ? "Requested, not available to watch yet. Tap the heart (favorite) to make Radarr search again."
                : "Requested, not released yet.";
            var item = Base(m, WantedFolderId + ":" + m.Id, "Not available yet", status);
            item.PremiereDate = FirstRelease(m);
            item.Tags = new List<string> { "Requested" };
            if (m.IsAvailable && m.Id > 0)
            {
                item.ProviderIds[ActionKey] = "search:" + m.Id.ToString(CultureInfo.InvariantCulture);
            }

            return item;
        }

        public static ChannelItemInfo Upcoming(Movie m, DateTime todayUtc)
        {
            var next = NextRelease(m, todayUtc);
            var status = next == null
                ? "Upcoming"
                : string.Format(CultureInfo.InvariantCulture, "Releases {0:yyyy-MM-dd} ({1})", next.Value.Date, next.Value.Kind);
            var shortStatus = next == null ? "Coming soon" : "Out " + next.Value.Date.ToString("MMM d", CultureInfo.InvariantCulture);
            if (m.HasFile)
            {
                status += ", already in your library";
                shortStatus = "In library";
            }
            else
            {
                status += ". Not available to watch yet.";
            }

            var item = Base(m, UpcomingFolderId + ":" + m.Id, shortStatus, status);
            item.PremiereDate = next?.Date ?? FirstRelease(m);
            item.Tags = new List<string> { "Upcoming" };
            return item;
        }

        public static ChannelItemInfo Queued(QueueItem q)
        {
            var movie = q.Movie ?? new Movie { Id = q.MovieId, Title = q.Title };
            var status = QueueStatus(q);
            var item = Base(movie, QueueFolderId + ":" + q.Id, status, status + ". Not available to watch yet.");
            item.PremiereDate = FirstRelease(movie);
            item.Tags = new List<string> { "Downloading" };
            return item;
        }

        /// <summary>A Discover movie. <paramref name="inRadarr"/> is the matching Radarr movie, if it was added already.</summary>
        public static ChannelItemInfo Discover(Movie m, Movie inRadarr, string folderId)
        {
            ChannelItemInfo item;
            if (inRadarr?.HasFile == true)
            {
                item = Base(m, folderId + ":" + m.TmdbId, "In library", "Already in your library. Watch it from your Movies library.");
            }
            else if (inRadarr != null)
            {
                item = Base(m, folderId + ":" + m.TmdbId, "Requested", "Requested, not available to watch yet.");
            }
            else
            {
                item = Base(m, folderId + ":" + m.TmdbId, "Tap ♥ to request", "Not in your library. Tap the heart (favorite) to request it.");
                item.ProviderIds[ActionKey] = "request:" + m.TmdbId.ToString(CultureInfo.InvariantCulture);
            }

            item.PremiereDate = FirstRelease(m);
            item.Tags = new List<string> { "Discover" };
            return item;
        }

        /// <summary>E.g. "Downloading 45%, 12 min left", "Paused 10%", "Importing", "Failed: ...".</summary>
        public static string QueueStatus(QueueItem q)
        {
            // (size - left) * 100 / size avoids 1 - 0.55 = 0.4499... rounding down to 44%.
            var percent = q.Size > 0 ? (int)Math.Floor(((q.Size - q.SizeLeft) * 100 / q.Size) + 1e-9) : 0;
            percent = Math.Max(0, Math.Min(100, percent));

            switch (q.TrackedDownloadState)
            {
                case "importPending":
                case "importing":
                    return "Importing";
                case "importBlocked":
                    return "Import blocked" + Suffix(q.ErrorMessage);
                case "failedPending":
                case "failed":
                    return "Failed" + Suffix(q.ErrorMessage);
            }

            switch (q.Status)
            {
                case "queued":
                    return "Queued";
                case "delay":
                    return "Delayed";
                case "paused":
                    return "Paused " + percent + "%";
                case "completed":
                    return "Downloaded, waiting for import";
                case "failed":
                    return "Failed" + Suffix(q.ErrorMessage);
                case "warning":
                    return "Downloading " + percent + "% (warning" + Suffix(q.ErrorMessage) + ")";
                case "downloadClientUnavailable":
                    return "Download client unavailable";
                default:
                    var left = FormatTimeLeft(q.TimeLeft);
                    return "Downloading " + percent + "%" + (left == null ? string.Empty : ", " + left + " left");
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
                    // Queue and Discover: keep Radarr's order. Requested/Upcoming: soonest release first.
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
                Name = (string.IsNullOrEmpty(m.Title) ? "Unknown movie" : m.Title) + " \u00b7 " + shortStatus,
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
            Add(list, m.InCinemas, "in cinemas");
            Add(list, m.DigitalRelease, "digital");
            Add(list, m.PhysicalRelease, "physical");
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
                return string.Format(CultureInfo.InvariantCulture, "{0} h {1} min", (int)t.TotalHours, t.Minutes);
            }

            return Math.Max(1, (int)Math.Round(t.TotalMinutes)) + " min";
        }

        private static string Suffix(string message) => string.IsNullOrWhiteSpace(message) ? string.Empty : ": " + message.Trim();
    }
}
