using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace EmbyRadarr.Radarr
{
    /// <summary>
    /// Short-lived in-memory cache for Radarr responses. Concurrent callers for the same key share one request.
    /// Failures are cached briefly too, so an offline Radarr isn't retried on every click.
    /// </summary>
    public class ResponseCache
    {
        private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(30);

        private readonly ConcurrentDictionary<string, Entry> entries = new ConcurrentDictionary<string, Entry>();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new ConcurrentDictionary<string, SemaphoreSlim>();
        private readonly Func<DateTime> clock;

        public ResponseCache(Func<DateTime> clock = null)
        {
            this.clock = clock ?? (() => DateTime.UtcNow);
        }

        public async Task<T> GetOrFetch<T>(string key, TimeSpan ttl, Func<Task<T>> fetch)
        {
            if (this.TryGetFresh(key, out var cached))
            {
                return Unwrap<T>(cached);
            }

            var gate = this.locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (this.TryGetFresh(key, out cached))
                {
                    return Unwrap<T>(cached);
                }

                Entry entry;
                try
                {
                    entry = new Entry { Value = await fetch().ConfigureAwait(false), Expires = this.clock() + ttl };
                }
                catch (RadarrException ex)
                {
                    entry = new Entry { Error = ex, Expires = this.clock() + FailureTtl };
                }

                this.entries[key] = entry;
                return Unwrap<T>(entry);
            }
            finally
            {
                gate.Release();
            }
        }

        public void Clear() => this.entries.Clear();

        private static T Unwrap<T>(Entry entry)
        {
            if (entry.Error != null)
            {
                throw new RadarrException(entry.Error.Message, entry.Error);
            }

            return (T)entry.Value;
        }

        private bool TryGetFresh(string key, out Entry entry)
            => this.entries.TryGetValue(key, out entry) && entry.Expires > this.clock();

        private class Entry
        {
            public object Value { get; set; }

            public RadarrException Error { get; set; }

            public DateTime Expires { get; set; }
        }
    }
}
