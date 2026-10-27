using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Channels;

namespace EmbyRadarr.Channel
{
    /// <summary>
    /// Emby only copies name, overview and provider ids onto an existing channel item when ForceUpdate is set,
    /// and every forced update also queues a metadata refresh. So force only items whose content changed since
    /// the last load (and everything once after an Emby restart).
    /// </summary>
    public static class ChangeTracker
    {
        private static readonly ConcurrentDictionary<string, string> Fingerprints = new ConcurrentDictionary<string, string>();

        public static void MarkChanged(IEnumerable<ChannelItemInfo> items)
        {
            foreach (var item in items)
            {
                var fingerprint = Fingerprint(item);
                item.ForceUpdate = !Fingerprints.TryGetValue(item.Id, out var previous) || previous != fingerprint;
                Fingerprints[item.Id] = fingerprint;
            }
        }

        private static string Fingerprint(ChannelItemInfo item)
            => item.Name + "\n" + item.Overview + "\n" + item.ImageUrl + "\n"
               + string.Join(";", (item.ProviderIds ?? new MediaBrowser.Model.Entities.ProviderIdDictionary()).OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value));
    }
}
