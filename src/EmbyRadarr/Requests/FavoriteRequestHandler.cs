using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EmbyRadarr.Channel;
using EmbyRadarr.Configuration;
using EmbyRadarr.Radarr;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;

namespace EmbyRadarr.Requests
{
    /// <summary>
    /// Turns "favorite" (the heart) on a channel movie into a Radarr action. Favoriting works in every Emby app,
    /// which makes it the one deliberate action regular users can take on a channel item.
    /// </summary>
    public class FavoriteRequestHandler : IServerEntryPoint
    {
        private static readonly TimeSpan RepeatWindow = TimeSpan.FromMinutes(10);

        private readonly IUserDataManager userDataManager;
        private readonly ConcurrentDictionary<string, DateTime> recent = new ConcurrentDictionary<string, DateTime>();

        public FavoriteRequestHandler(IUserDataManager userDataManager)
        {
            this.userDataManager = userDataManager;
        }

        public void Run() => this.userDataManager.UserDataSaved += this.OnUserDataSaved;

        public void Dispose() => this.userDataManager.UserDataSaved -= this.OnUserDataSaved;

        /// <summary>Reads the action from our provider id, or from the channel item id as a fallback.</summary>
        public static FavoriteAction ParseAction(BaseItem item)
        {
            if (item?.ProviderIds != null && item.ProviderIds.TryGetValue(ItemBuilder.ActionKey, out var value))
            {
                return FavoriteAction.Parse(value);
            }

            // Channel items keep the id we gave them (e.g. "discover-trending:603") as ExternalId.
            var externalId = item?.ExternalId ?? string.Empty;
            var colon = externalId.IndexOf(':');
            if (colon > 0 && externalId.StartsWith(ItemBuilder.DiscoverFolderId + "-", StringComparison.Ordinal))
            {
                return FavoriteAction.Parse("request:" + externalId.Substring(colon + 1));
            }

            return null;
        }

        public static bool MayRequest(PluginConfiguration config, User user)
            => config.RequestAccess == RequestAccess.Everyone || user?.Policy?.IsAdministrator == true;

        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            if (e.UserData?.IsFavorite != true)
            {
                return;
            }

            var action = ParseAction(e.Item);
            var plugin = Plugin.Instance;
            if (action == null || plugin == null)
            {
                return;
            }

            var userName = e.User?.Name ?? "unknown user";
            if (!MayRequest(plugin.Configuration, e.User))
            {
                plugin.Logger.Info("{0} favorited {1}, but only administrators may request movies", userName, e.Item?.Name);
                return;
            }

            // Emby saves user data for other reasons too (e.g. playback) while the heart stays on.
            var key = userName + "|" + action;
            var now = DateTime.UtcNow;
            if (this.recent.TryGetValue(key, out var last) && now - last < RepeatWindow)
            {
                return;
            }

            this.recent[key] = now;
            Task.Run(() => Execute(plugin, action, userName));
        }

        private static async Task Execute(Plugin plugin, FavoriteAction action, string userName)
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    if (action.Kind == FavoriteAction.Request)
                    {
                        var result = await plugin.Data.RequestMovie(action.Id, cts.Token).ConfigureAwait(false);
                        plugin.Logger.Info("{0}: {1}", userName, result);
                    }
                    else
                    {
                        await plugin.Data.SearchMovie(action.Id, cts.Token).ConfigureAwait(false);
                        plugin.Logger.Info("{0} asked Radarr to search for movie {1}", userName, action.Id);
                    }
                }
            }
            catch (RadarrException ex)
            {
                plugin.Logger.Warn("{0} favorited a movie, but the Radarr action failed: {1}", userName, ex.Message);
            }
            catch (Exception ex)
            {
                plugin.Logger.ErrorException("Unexpected error handling a favorite from {0}", ex, userName);
            }
        }
    }

    public class FavoriteAction
    {
        public const string Request = "request";
        public const string Search = "search";

        public string Kind { get; private set; }

        public int Id { get; private set; }

        public static FavoriteAction Parse(string value)
        {
            var parts = (value ?? string.Empty).Split(':');
            if (parts.Length == 2
                && (parts[0] == Request || parts[0] == Search)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id > 0)
            {
                return new FavoriteAction { Kind = parts[0], Id = id };
            }

            return null;
        }

        public override string ToString() => this.Kind + ":" + this.Id.ToString(CultureInfo.InvariantCulture);
    }
}
