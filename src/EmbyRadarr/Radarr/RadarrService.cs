using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmbyRadarr.Configuration;

namespace EmbyRadarr.Radarr
{
    /// <summary>Cached reads and the few write actions (request, search) used by the channel and the settings page.</summary>
    public class RadarrService
    {
        public const int UpcomingDays = 90;

        private readonly RadarrClient client;
        private readonly Func<PluginConfiguration> config;
        private readonly ResponseCache cache;
        private readonly Func<DateTime> clock;

        public RadarrService(RadarrClient client, Func<PluginConfiguration> config, ResponseCache cache = null, Func<DateTime> clock = null)
        {
            this.client = client;
            this.config = config;
            this.cache = cache ?? new ResponseCache();
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>Raised after a write to Radarr, so the channel can tell Emby to reload.</summary>
        public event EventHandler Changed;

        public RadarrClient Client => this.client;

        private RadarrConnection Connection => RadarrConnection.FromConfig(this.config());

        public Task<List<Movie>> GetMovies(CancellationToken token)
            => this.Cached("movies", conn => this.client.GetMovies(conn, token));

        public async Task<List<Movie>> GetWanted(CancellationToken token)
            => (await this.GetMovies(token).ConfigureAwait(false)).Where(m => m.Monitored && !m.HasFile).ToList();

        public Task<List<Movie>> GetUpcoming(CancellationToken token)
        {
            var today = this.clock().Date;
            return this.Cached("calendar:" + today.ToString("yyyyMMdd"), conn => this.client.GetCalendar(conn, today, today.AddDays(UpcomingDays), token));
        }

        public Task<List<QueueItem>> GetQueue(CancellationToken token)
            => this.Cached("queue", async conn => (await this.client.GetQueue(conn, token).ConfigureAwait(false))?.Records ?? new List<QueueItem>());

        public Task<List<Movie>> GetDiscover(CancellationToken token)
            => this.Cached("discover", async conn => (await this.client.GetDiscover(conn, token).ConfigureAwait(false)).Where(m => !m.IsExcluded && m.TmdbId > 0).ToList());

        /// <summary>Adds a movie to Radarr (with the configured profile and root folder) and starts a search.</summary>
        /// <returns>A short description of what happened.</returns>
        public async Task<string> RequestMovie(int tmdbId, CancellationToken token)
        {
            var cfg = this.config();
            if (cfg.QualityProfileId <= 0 || string.IsNullOrEmpty(cfg.RootFolderPath))
            {
                throw new RadarrException("Choose a quality profile and root folder in the plugin settings before requesting movies.");
            }

            var conn = this.Connection;
            var existing = (await this.client.GetMovies(conn, token).ConfigureAwait(false)).FirstOrDefault(m => m.TmdbId == tmdbId);
            if (existing != null)
            {
                return existing.Title + " is already in Radarr.";
            }

            var movie = await this.client.LookupTmdb(conn, tmdbId, token).ConfigureAwait(false);
            if (movie == null || movie.TmdbId != tmdbId || string.IsNullOrEmpty(movie.Title))
            {
                throw new RadarrException("Radarr could not find TMDb id " + tmdbId + ".");
            }

            await this.client.AddMovie(conn, new AddMovieRequest
            {
                Title = movie.Title,
                TmdbId = movie.TmdbId,
                Year = movie.Year,
                QualityProfileId = cfg.QualityProfileId,
                RootFolderPath = cfg.RootFolderPath,
                Images = movie.Images,
            }, token).ConfigureAwait(false);

            this.OnChanged();
            return "Requested " + movie.Title + " (" + movie.Year + ").";
        }

        /// <summary>Asks Radarr to search for one movie now.</summary>
        public async Task SearchMovie(int radarrMovieId, CancellationToken token)
        {
            await this.client.SendCommand(this.Connection, new CommandRequest { Name = "MoviesSearch", MovieIds = new List<int> { radarrMovieId } }, token).ConfigureAwait(false);
            this.OnChanged();
        }

        /// <summary>Asks Radarr to search for all missing (wanted) movies.</summary>
        public async Task SearchAllWanted(CancellationToken token)
        {
            await this.client.SendCommand(this.Connection, new CommandRequest { Name = "MissingMoviesSearch" }, token).ConfigureAwait(false);
            this.OnChanged();
        }

        public void ClearCache() => this.cache.Clear();

        private void OnChanged()
        {
            this.cache.Clear();
            this.Changed?.Invoke(this, EventArgs.Empty);
        }

        private Task<T> Cached<T>(string what, Func<RadarrConnection, Task<T>> fetch)
        {
            var conn = this.Connection;
            var ttl = TimeSpan.FromMinutes(this.config().CacheMinutesClamped);
            return this.cache.GetOrFetch(conn.BaseUrl + "|" + what, ttl, () => fetch(conn));
        }
    }
}
