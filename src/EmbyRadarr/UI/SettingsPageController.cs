using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Plugins.UI.Views;

namespace EmbyRadarr.UI
{
    internal class SettingsPageController : IPluginUIPageController
    {
        private readonly PluginInfo pluginInfo;
        private readonly Plugin plugin;

        public SettingsPageController(PluginInfo pluginInfo, Plugin plugin)
        {
            this.pluginInfo = pluginInfo;
            this.plugin = plugin;
            this.PageInfo = new PluginPageInfo
            {
                Name = "RadarrSettings",
                DisplayName = "Radarr",
                EnableInMainMenu = true,
                MenuIcon = "movie",
                IsMainConfigPage = true,
            };
        }

        public PluginPageInfo PageInfo { get; }

        public string PluginId => this.pluginInfo.Id;

        public Task Initialize(CancellationToken token) => Task.CompletedTask;

        public async Task<IPluginUIView> CreateDefaultPageView()
        {
            var view = new SettingsPageView(this.pluginInfo, this.plugin);
            await view.LoadRadarrChoices().ConfigureAwait(false);
            return view;
        }
    }
}
