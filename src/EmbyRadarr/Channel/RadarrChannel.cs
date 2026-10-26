using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmbyRadarr.Radarr;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;

namespace EmbyRadarr.Channel
{
    /// <summary>
    /// The Radarr channel: Discover (request by tapping the heart), Requested, Coming soon and Downloading.
    /// Browsing only reads from Radarr; requests are handled by <see cref="Requests.FavoriteRequestHandler"/>.
    /// </summary>
    public class RadarrChannel : IChannel, IHasChangeEvent, IRequiresMediaInfoCallback, IDisposable
    {
        private static readonly TimeSpan BackgroundRefresh = TimeSpan.FromMinutes(5);

        private readonly Timer timer;
        private string lastFingerprint;

        public RadarrChannel()
        {
            // Polls Radarr in the background and tells Emby when something changed (IHasChangeEvent).
            this.timer = new Timer(_ => this.CheckForChanges(), null, TimeSpan.FromMinutes(1), BackgroundRefresh);
            Current = this;
        }

        public event EventHandler ContentChanged;

        /// <summary>The instance Emby created, so other parts of the plugin can ask for a refresh.</summary>
        internal static RadarrChannel Current { get; private set; }

        public string Name => "Radarr";

        public string Description => "Request movies and follow their status. Tap the heart on a movie in Discover to request it.";

        public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

        private static Plugin Plugin => Plugin.Instance;

        private static RadarrService Data => Plugin.Data;

        /// <summary>Tells Emby to reload the channel, e.g. after the settings changed or a movie was requested.</summary>
        internal void NotifyChanged()
        {
            this.lastFingerprint = null;
            this.ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var folderId = query.FolderId;
            if (!Plugin.Configuration.IsConfigured)
            {
                return Single(NotConfigured());
            }

            if (string.IsNullOrEmpty(folderId))
            {
                return RootFolders();
            }

            if (folderId == ItemBuilder.DiscoverFolderId)
            {
                return Single(
                    Folder(ItemBuilder.TrendingFolderId, "Trending", "Movies trending right now. Tap the heart on a movie to request it."),
                    Folder(ItemBuilder.PopularFolderId, "Popular", "Popular movies. Tap the heart on a movie to request it."),
                    Folder(ItemBuilder.RecommendedFolderId, "Recommended", "Recommended based on your Radarr library. Tap the heart on a movie to request it."));
            }

            List<ChannelItemInfo> items;
            try
            {
                switch (folderId)
                {
                    case ItemBuilder.TrendingFolderId:
                    case ItemBuilder.PopularFolderId:
                    case ItemBuilder.RecommendedFolderId:
                        items = await DiscoverItems(folderId, cancellationToken).ConfigureAwait(false);
                        break;
                    case ItemBuilder.WantedFolderId:
                        items = (await Data.GetWanted(cancellationToken).ConfigureAwait(false)).Select(ItemBuilder.Wanted).ToList();
                        break;
                    case ItemBuilder.UpcomingFolderId:
                        var today = DateTime.UtcNow.Date;
                        items = (await Data.GetUpcoming(cancellationToken).ConfigureAwait(false)).Select(m => ItemBuilder.Upcoming(m, today)).ToList();
                        break;
                    case ItemBuilder.QueueFolderId:
                        items = (await Data.GetQueue(cancellationToken).ConfigureAwait(false)).Select(ItemBuilder.Queued).ToList();
                        break;
                    default:
                        // Message items are folders too; opening one shows nothing.
                        return Single();
                }
            }
            catch (RadarrException ex)
            {
                return Single(ItemBuilder.Message("error", "Radarr unavailable", ex.Message));
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Plugin.Logger.ErrorException("Unexpected error while loading channel folder {0}", ex, folderId);
                return Single(ItemBuilder.Message("error", "Error loading Radarr data", ex.Message));
            }

            if (items.Count == 0)
            {
                return Single(ItemBuilder.Message("empty", EmptyTitle(folderId), "Nothing here right now."));
            }

            return ItemBuilder.Page(items, query, folderId);
        }

        public Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
        {
            // None of these movies are downloaded: there is nothing to play.
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        public IEnumerable<ImageType> GetSupportedChannelImages() => new[] { ImageType.Primary, ImageType.Thumb };

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            var assembly = typeof(RadarrChannel).Assembly;
            return Task.FromResult(new DynamicImageResponse
            {
                Format = ImageFormat.Png,
                Stream = assembly.GetManifestResourceStream("EmbyRadarr.Images.channel.png"),
            });
        }

        public void Dispose() => this.timer.Dispose();

        private static async Task<List<ChannelItemInfo>> DiscoverItems(string folderId, CancellationToken token)
        {
            var discover = await Data.GetDiscover(token).ConfigureAwait(false);
            var inRadarr = (await Data.GetMovies(token).ConfigureAwait(false))
                .Where(m => m.TmdbId > 0)
                .GroupBy(m => m.TmdbId)
                .ToDictionary(g => g.Key, g => g.First());

            Func<Movie, bool> filter;
            switch (folderId)
            {
                case ItemBuilder.TrendingFolderId: filter = m => m.IsTrending; break;
                case ItemBuilder.PopularFolderId: filter = m => m.IsPopular; break;
                default: filter = m => m.IsRecommendation; break;
            }

            return discover
                .Where(filter)
                .Select(m => ItemBuilder.Discover(m, inRadarr.TryGetValue(m.TmdbId, out var r) ? r : null, folderId))
                .ToList();
        }

        private async void CheckForChanges()
        {
            try
            {
                if (Plugin?.Configuration.IsConfigured != true)
                {
                    return;
                }

                var movies = await Data.GetMovies(CancellationToken.None).ConfigureAwait(false);
                var queue = await Data.GetQueue(CancellationToken.None).ConfigureAwait(false);

                var fingerprint = string.Join(",", movies.Select(m => m.Id + (m.HasFile ? "f" : string.Empty) + (m.IsAvailable ? "a" : string.Empty)))
                    + "|" + string.Join(",", queue.Select(q => q.Id + ItemBuilder.QueueStatus(q)));

                if (this.lastFingerprint != null && fingerprint != this.lastFingerprint)
                {
                    this.ContentChanged?.Invoke(this, EventArgs.Empty);
                }

                this.lastFingerprint = fingerprint;
            }
            catch (RadarrException)
            {
                // Radarr offline: the channel itself shows the message when browsed.
            }
            catch (Exception ex)
            {
                Plugin?.Logger.ErrorException("Background refresh failed", ex);
            }
        }

        private static ChannelItemResult RootFolders()
            => Single(
                Folder(ItemBuilder.DiscoverFolderId, "Discover", "Find movies to request. Tap the heart (favorite) on a movie to request it."),
                Folder(ItemBuilder.WantedFolderId, "Requested", "Requested movies that are not available to watch yet."),
                Folder(ItemBuilder.UpcomingFolderId, "Coming soon", "Requested movies with a release in the next " + RadarrService.UpcomingDays + " days."),
                Folder(ItemBuilder.QueueFolderId, "Downloading", "Movies downloading right now, with progress."));

        private static ChannelItemInfo Folder(string id, string name, string overview)
            => new ChannelItemInfo { Id = id, Name = name, Overview = overview, Type = ChannelItemType.Folder, FolderType = ChannelFolderType.Container };

        private static ChannelItemInfo NotConfigured()
            => ItemBuilder.Message("setup", "Radarr is not configured", "An administrator needs to open Dashboard > Plugins > Radarr, enter the Radarr URL and API key, test and save.");

        private static string EmptyTitle(string folderId)
        {
            switch (folderId)
            {
                case ItemBuilder.WantedFolderId: return "No requested movies";
                case ItemBuilder.UpcomingFolderId: return "Nothing coming soon";
                case ItemBuilder.QueueFolderId: return "Nothing downloading";
                default: return "No movies found";
            }
        }

        private static ChannelItemResult Single(params ChannelItemInfo[] items)
            => new ChannelItemResult { Items = items.ToList(), TotalRecordCount = items.Length };
    }
}
