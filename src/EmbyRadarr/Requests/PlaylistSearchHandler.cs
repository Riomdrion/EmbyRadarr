using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmbyRadarr.Radarr;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Plugins;

namespace EmbyRadarr.Requests
{
    /// <summary>
    /// Free-text search for regular users: a new playlist named "Zoek: titel" is the one place every Emby app lets
    /// a user type text the server can see. The results show up in the channel's "Zoeken" folder. The playlist is
    /// renamed as feedback and removed again after an hour.
    /// </summary>
    public class PlaylistSearchHandler : IServerEntryPoint
    {
        private static readonly string[] Prefixes = { "zoek:", "zoeken:", "zoek ", "search:" };
        private static readonly TimeSpan KeepPlaylist = TimeSpan.FromHours(1);

        private readonly ILibraryManager libraryManager;
        private readonly ConcurrentDictionary<long, DateTime> handled = new ConcurrentDictionary<long, DateTime>();
        private Timer cleanup;

        public PlaylistSearchHandler(ILibraryManager libraryManager)
        {
            this.libraryManager = libraryManager;
        }

        public void Run()
        {
            this.libraryManager.ItemAdded += this.OnItemAdded;
            this.cleanup = new Timer(_ => this.RemoveOldPlaylists(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        }

        public void Dispose()
        {
            this.libraryManager.ItemAdded -= this.OnItemAdded;
            this.cleanup?.Dispose();
        }

        /// <summary>"Zoek: Dune" -> "Dune"; null when the name isn't a search.</summary>
        public static string ParseSearchTerm(string playlistName)
        {
            var name = (playlistName ?? string.Empty).Trim();
            foreach (var prefix in Prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var term = name.Substring(prefix.Length).Trim();
                    return term.Length >= 2 ? term : null;
                }
            }

            return null;
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (!(e.Item is Playlist playlist))
            {
                return;
            }

            var term = ParseSearchTerm(playlist.Name);
            var plugin = Plugin.Instance;
            if (term == null || plugin == null || !plugin.Configuration.IsConfigured)
            {
                return;
            }

            Task.Run(() => this.Search(plugin, playlist, e.Parent, term));
        }

        private async Task Search(Plugin plugin, Playlist playlist, BaseItem parent, string term)
        {
            string newName;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var result = await plugin.Data.Search(term, cts.Token).ConfigureAwait(false);
                    newName = result.Movies.Count == 0
                        ? "✗ Niets gevonden voor “" + term + "”"
                        : "✓ " + result.Movies.Count + " films gevonden voor “" + term + "”: kijk bij Films aanvragen › Zoeken";
                    plugin.Logger.Info("Search for \"{0}\": {1} results", term, result.Movies.Count);
                }
            }
            catch (Exception ex)
            {
                newName = "✗ Zoeken lukte niet, probeer het later opnieuw";
                plugin.Logger.Warn("Search for \"{0}\" failed: {1}", term, ex.Message);
            }

            try
            {
                playlist.Name = newName;
                this.libraryManager.UpdateItem(playlist, parent ?? playlist.GetParent(), MediaBrowser.Controller.Library.ItemUpdateType.MetadataEdit);
                this.handled[playlist.InternalId] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                plugin.Logger.ErrorException("Could not rename search playlist", ex);
            }
        }

        private void RemoveOldPlaylists()
        {
            foreach (var entry in this.handled.Where(h => DateTime.UtcNow - h.Value > KeepPlaylist).ToList())
            {
                try
                {
                    var item = this.libraryManager.GetItemById(entry.Key);
                    if (item is Playlist)
                    {
                        this.libraryManager.DeleteItem(item, new DeleteOptions { DeleteFileLocation = true });
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Instance?.Logger.Warn("Could not remove search playlist: {0}", ex.Message);
                }

                this.handled.TryRemove(entry.Key, out _);
            }
        }
    }
}
