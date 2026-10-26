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
    /// Read-only channel with three folders: Wanted, Upcoming and Download queue.
    /// Only GET requests are sent to Radarr.
    /// </summary>
    public class RadarrChannel : IChannel, IHasChangeEvent, IRequiresMediaInfoCallback, IDisposable
    {
        private const int UpcomingDays = 90;
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

        /// <summary>The instance Emby created, so the settings page can ask for a refresh after saving.</summary>
        internal static RadarrChannel Current { get; private set; }

        /// <summary>Tells Emby to reload the channel, e.g. after the settings changed.</summary>
        internal void NotifyChanged()
        {
            this.lastFingerprint = null;
            this.ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        public string Name => "Radarr";

        public string Description => "Wanted, upcoming and downloading movies from Radarr.";

        public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

        private static Plugin Plugin => Plugin.Instance;

        public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var folderId = query.FolderId;
            if (string.IsNullOrEmpty(folderId))
            {
                return RootFolders();
            }

            if (!Plugin.Configuration.IsConfigured)
            {
                return Single(NotConfigured());
            }

            List<ChannelItemInfo> items;
            try
            {
                switch (folderId)
                {
                    case ItemBuilder.WantedFolderId:
                        items = (await this.GetWanted(cancellationToken).ConfigureAwait(false)).Select(ItemBuilder.Wanted).ToList();
                        break;
                    case ItemBuilder.UpcomingFolderId:
                        var today = DateTime.UtcNow.Date;
                        items = (await this.GetUpcoming(cancellationToken).ConfigureAwait(false)).Select(m => ItemBuilder.Upcoming(m, today)).ToList();
                        break;
                    case ItemBuilder.QueueFolderId:
                        items = (await this.GetQueue(cancellationToken).ConfigureAwait(false)).Select(ItemBuilder.Queued).ToList();
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
                return Single(ItemBuilder.Message("empty", EmptyTitle(folderId), "Radarr returned no movies for this list."));
            }

            return ItemBuilder.Page(items, query, folderId);
        }

        public Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
        {
            // These are movies that are not downloaded yet: there is nothing to play.
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

        internal Task<List<Movie>> GetWanted(CancellationToken token)
            => Fetch("movies", async (radarr, conn) => ItemBuilder.SelectWanted(await radarr.GetMovies(conn, token).ConfigureAwait(false)).ToList());

        internal Task<List<Movie>> GetUpcoming(CancellationToken token)
        {
            var today = DateTime.UtcNow.Date;
            return Fetch("calendar:" + today.ToString("yyyyMMdd"), (radarr, conn) => radarr.GetCalendar(conn, today, today.AddDays(UpcomingDays), token));
        }

        internal Task<List<QueueItem>> GetQueue(CancellationToken token)
            => Fetch("queue", async (radarr, conn) => (await radarr.GetQueue(conn, token).ConfigureAwait(false))?.Records ?? new List<QueueItem>());

        private static Task<T> Fetch<T>(string what, Func<RadarrClient, RadarrConnection, Task<T>> fetch)
        {
            var conn = RadarrConnection.FromConfig(Plugin.Configuration);
            var ttl = TimeSpan.FromMinutes(Plugin.Configuration.CacheMinutesClamped);
            return Plugin.Cache.GetOrFetch(conn.BaseUrl + "|" + what, ttl, () => fetch(Plugin.Radarr, conn));
        }

        private async void CheckForChanges()
        {
            try
            {
                if (Plugin?.Configuration.IsConfigured != true)
                {
                    return;
                }

                var wanted = await this.GetWanted(CancellationToken.None).ConfigureAwait(false);
                var upcoming = await this.GetUpcoming(CancellationToken.None).ConfigureAwait(false);
                var queue = await this.GetQueue(CancellationToken.None).ConfigureAwait(false);

                var fingerprint = string.Join(",", wanted.Select(m => "w" + m.Id + m.IsAvailable))
                    + "|" + string.Join(",", upcoming.Select(m => "u" + m.Id + m.HasFile))
                    + "|" + string.Join(",", queue.Select(q => "q" + q.Id + ItemBuilder.QueueStatus(q)));

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
        {
            if (!Plugin.Configuration.IsConfigured)
            {
                return Single(NotConfigured());
            }

            return Single(
                Folder(ItemBuilder.WantedFolderId, "Wanted", "Monitored movies without a file yet."),
                Folder(ItemBuilder.UpcomingFolderId, "Upcoming", "Releases in the next " + UpcomingDays + " days (Radarr calendar)."),
                Folder(ItemBuilder.QueueFolderId, "Download queue", "Movies currently downloading, with progress."));
        }

        private static ChannelItemInfo Folder(string id, string name, string overview)
            => new ChannelItemInfo { Id = id, Name = name, Overview = overview, Type = ChannelItemType.Folder, FolderType = ChannelFolderType.Container };

        private static ChannelItemInfo NotConfigured()
            => ItemBuilder.Message("setup", "Radarr is not configured", "Open Dashboard > Plugins > Radarr, enter the Radarr URL and API key, test and save.");

        private static string EmptyTitle(string folderId)
        {
            switch (folderId)
            {
                case ItemBuilder.WantedFolderId: return "No wanted movies";
                case ItemBuilder.UpcomingFolderId: return "No upcoming releases";
                default: return "Download queue is empty";
            }
        }

        private static ChannelItemResult Single(params ChannelItemInfo[] items)
            => new ChannelItemResult { Items = items.ToList(), TotalRecordCount = items.Length };
    }
}
