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

        public string Name => "Films aanvragen";

        public string Description => "Vraag films aan en volg hoe ver ze zijn. Tik op het hartje bij een film om hem aan te vragen.";

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
                    Folder(ItemBuilder.TrendingFolderId, "Nu populair", "Films waar nu veel over gepraat wordt. Tik op het hartje om er een aan te vragen."),
                    Folder(ItemBuilder.PopularFolderId, "Populair", "Populaire films. Tik op het hartje om er een aan te vragen."),
                    Folder(ItemBuilder.RecommendedFolderId, "Aanbevolen", "Aanbevolen op basis van de films die er al zijn. Tik op het hartje om er een aan te vragen."));
            }

            if (folderId == ItemBuilder.SearchRootFolderId)
            {
                return SearchFolders();
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
                        if (folderId.StartsWith(ItemBuilder.SearchFolderPrefix, StringComparison.Ordinal))
                        {
                            items = await SearchItems(folderId, cancellationToken).ConfigureAwait(false);
                            break;
                        }

                        // Message items are folders too; opening one shows nothing.
                        return Single();
                }
            }
            catch (RadarrException ex)
            {
                Plugin.Logger.Warn("Channel folder {0}: {1}", folderId, ex.Message);
                return Single(ItemBuilder.Message("error", "Aanvragen werkt nu even niet", "Probeer het later opnieuw. (" + ex.Message + ")"));
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Plugin.Logger.ErrorException("Unexpected error while loading channel folder {0}", ex, folderId);
                return Single(ItemBuilder.Message("error", "Aanvragen werkt nu even niet", "Probeer het later opnieuw."));
            }

            if (items.Count == 0)
            {
                return Single(ItemBuilder.Message("empty", EmptyTitle(folderId), "Hier staat nu niets."));
            }

            ChangeTracker.MarkChanged(items);
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
            var inRadarr = await RequestedByTmdb(token).ConfigureAwait(false);

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

                // First run after a (re)start also counts as a change: Emby keeps channel folders from earlier
                // plugin versions in its database, so a plugin update would otherwise not show new folders.
                if (fingerprint != this.lastFingerprint)
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
                Folder(ItemBuilder.DiscoverFolderId, "Ontdekken", "Populaire en aanbevolen films. Tik op het hartje (favoriet) bij een film om hem aan te vragen."),
                Folder(ItemBuilder.SearchRootFolderId, "Zoeken", SearchHelp),
                Folder(ItemBuilder.WantedFolderId, "Aangevraagd", "Films die zijn aangevraagd, maar nog niet te kijken zijn."),
                Folder(ItemBuilder.UpcomingFolderId, "Binnenkort", "Aangevraagde films die de komende " + RadarrService.UpcomingDays + " dagen uitkomen."),
                Folder(ItemBuilder.QueueFolderId, "Wordt binnengehaald", "Films die nu worden gedownload, met hoe ver ze zijn."));

        private const string SearchHelp = "Een specifieke film zoeken? Maak een afspeellijst met de naam \u201cZoek: titel\u201d, "
            + "bijvoorbeeld \u201cZoek: Dune\u201d. Binnen een paar seconden staan de resultaten hier. Tik op het hartje bij de juiste film om hem aan te vragen.";

        private static ChannelItemResult SearchFolders()
        {
            var searches = Data.Searches;
            var items = new List<ChannelItemInfo> { ItemBuilder.Message("search-help", "Zo zoek je een film", SearchHelp) };
            items.AddRange(searches.Select(s => Folder(
                ItemBuilder.SearchFolderPrefix + s.Id,
                "\u201c" + s.Term + "\u201d (" + s.Movies.Count + " films)",
                "Gezocht op " + ItemBuilder.LongDate(s.SearchedUtc) + ". Tik op het hartje bij een film om hem aan te vragen.")));
            return Single(items.ToArray());
        }

        private static async Task<List<ChannelItemInfo>> SearchItems(string folderId, CancellationToken token)
        {
            var search = int.TryParse(folderId.Substring(ItemBuilder.SearchFolderPrefix.Length), out var id) ? Data.GetSearch(id) : null;
            if (search == null)
            {
                return new List<ChannelItemInfo>();
            }

            var inRadarr = await RequestedByTmdb(token).ConfigureAwait(false);
            return search.Movies.Select(m => ItemBuilder.Discover(m, inRadarr.TryGetValue(m.TmdbId, out var r) ? r : null, folderId)).ToList();
        }

        private static async Task<Dictionary<int, Movie>> RequestedByTmdb(CancellationToken token)
            => (await Data.GetMovies(token).ConfigureAwait(false))
                .Where(m => m.TmdbId > 0)
                .GroupBy(m => m.TmdbId)
                .ToDictionary(g => g.Key, g => g.First());

        private static ChannelItemInfo Folder(string id, string name, string overview)
            => new ChannelItemInfo { Id = id, Name = name, Overview = overview, Type = ChannelItemType.Folder, FolderType = ChannelFolderType.Container };

        private static ChannelItemInfo NotConfigured()
            => ItemBuilder.Message("setup", "Aanvragen is nog niet ingesteld", "Een beheerder moet dit nog instellen (Dashboard > Plugins > Radarr).");

        private static string EmptyTitle(string folderId)
        {
            switch (folderId)
            {
                case ItemBuilder.WantedFolderId: return "Geen aangevraagde films";
                case ItemBuilder.UpcomingFolderId: return "Er komt binnenkort niets uit";
                case ItemBuilder.QueueFolderId: return "Er wordt nu niets binnengehaald";
                default: return "Geen films gevonden";
            }
        }

        private static ChannelItemResult Single(params ChannelItemInfo[] items)
            => new ChannelItemResult { Items = items.ToList(), TotalRecordCount = items.Length };
    }
}
