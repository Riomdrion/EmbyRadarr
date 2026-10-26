using System;
using System.Collections.Generic;
using EmbyRadarr.Configuration;
using EmbyRadarr.Radarr;
using EmbyRadarr.UI;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Serialization;

namespace EmbyRadarr
{
    /// <summary>
    /// Plugin entry. Settings are stored by BasePlugin&lt;T&gt; in the standard plugin
    /// configuration file; the settings page uses the SDK's GenericEdit UI (IHasUIPages),
    /// which supports buttons without hand-written HTML/JS.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasUIPages
    {
        public const string PluginName = "Radarr";

        private readonly ILogger logger;
        private readonly RadarrClient radarrClient;
        private List<IPluginUIPageController> pages;

        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            IHttpClient httpClient,
            ILogManager logManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            this.logger = logManager.GetLogger(PluginName);
            this.radarrClient = new RadarrClient(httpClient, this.logger);
        }

        public static Plugin Instance { get; private set; }

        public override string Name => PluginName;

        public override string Description => "Shows Radarr's wanted, upcoming and downloading movies in Emby.";

        public override Guid Id => new Guid("b7dda718-5f4f-46cd-82a0-96dddf68f54b");

        public ILogger Logger => this.logger;

        public RadarrClient Radarr => this.radarrClient;

        public IReadOnlyCollection<IPluginUIPageController> UIPageControllers
        {
            get
            {
                if (this.pages == null)
                {
                    this.pages = new List<IPluginUIPageController>
                    {
                        new SettingsPageController(this.GetPluginInfo(), this),
                    };
                }

                return this.pages.AsReadOnly();
            }
        }
    }
}
