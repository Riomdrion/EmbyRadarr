using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using EmbyRadarr.Radarr;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;

namespace EmbyRadarr.UI
{
    internal class SettingsPageView : PluginPageView
    {
        private readonly Plugin plugin;

        // A key typed in the form but not saved yet. Kept server-side so the form only ever holds the mask.
        private string pendingApiKey;

        public SettingsPageView(PluginInfo pluginInfo, Plugin plugin)
            : base(pluginInfo.Id)
        {
            this.plugin = plugin;
            var config = plugin.Configuration;

            this.ContentData = new SettingsUI
            {
                RadarrUrl = config.RadarrUrl,
                ApiKey = string.IsNullOrEmpty(config.ApiKey) ? string.Empty : SettingsUI.ApiKeyMask,
                QualityProfileId = config.QualityProfileId > 0 ? config.QualityProfileId.ToString() : null,
                RootFolderPath = config.RootFolderPath,
                CacheMinutes = config.CacheMinutesClamped,
            };
        }

        private SettingsUI Ui => (SettingsUI)this.ContentData;

        /// <summary>Fills the dropdowns when the page opens. Failure is shown in the status line, never thrown.</summary>
        public async Task LoadRadarrChoices()
        {
            if (!this.plugin.Configuration.IsConfigured)
            {
                this.AddSavedValuesAsOptions();
                return;
            }

            await this.TestAndLoad().ConfigureAwait(false);
        }

        public override async Task<IPluginUIView> RunCommand(string itemId, string commandId, string data)
        {
            if (commandId == SettingsUI.TestCommand)
            {
                await this.TestAndLoad().ConfigureAwait(false);
                return this;
            }

            return await base.RunCommand(itemId, commandId, data).ConfigureAwait(false);
        }

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            var ui = this.Ui;
            var config = this.plugin.Configuration;

            config.RadarrUrl = (ui.RadarrUrl ?? string.Empty).Trim().TrimEnd('/');
            config.ApiKey = this.ResolveApiKey();
            config.QualityProfileId = int.TryParse(ui.QualityProfileId, out var id) ? id : 0;
            config.RootFolderPath = ui.RootFolderPath ?? string.Empty;
            config.CacheMinutes = Math.Max(1, Math.Min(5, ui.CacheMinutes));
            this.plugin.SaveConfiguration();
            this.plugin.Cache.Clear();

            this.pendingApiKey = null;
            ui.ApiKey = string.IsNullOrEmpty(config.ApiKey) ? string.Empty : SettingsUI.ApiKeyMask;
            this.plugin.Logger.Info("Settings saved (Radarr URL: {0})", config.RadarrUrl);

            return base.OnSaveCommand(itemId, commandId, data);
        }

        private string ResolveApiKey()
        {
            var typed = this.Ui.ApiKey?.Trim();
            if (typed == SettingsUI.ApiKeyMask)
            {
                return this.pendingApiKey ?? this.plugin.Configuration.ApiKey;
            }

            return typed ?? string.Empty;
        }

        private async Task TestAndLoad()
        {
            var ui = this.Ui;
            var apiKey = this.ResolveApiKey();
            if (ui.ApiKey?.Trim() != SettingsUI.ApiKeyMask && !string.IsNullOrEmpty(apiKey))
            {
                // Remember the newly typed key, but don't echo it back to the browser.
                this.pendingApiKey = apiKey;
                ui.ApiKey = SettingsUI.ApiKeyMask;
            }

            var conn = new RadarrConnection(ui.RadarrUrl, apiKey);
            var radarr = this.plugin.Radarr;

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    var status = await radarr.GetSystemStatus(conn, cts.Token).ConfigureAwait(false);
                    var profiles = await radarr.GetQualityProfiles(conn, cts.Token).ConfigureAwait(false);
                    var folders = await radarr.GetRootFolders(conn, cts.Token).ConfigureAwait(false);

                    ui.QualityProfileOptions = profiles
                        .OrderBy(p => p.Name)
                        .Select(p => new EditorSelectOption { Value = p.Id.ToString(), Name = p.Name, IsEnabled = true })
                        .ToList();
                    ui.RootFolderOptions = folders
                        .Select(f => new EditorSelectOption { Value = f.Path, Name = f.Accessible ? f.Path : f.Path + " (not accessible)", IsEnabled = true })
                        .ToList();
                    this.AddSavedValuesAsOptions();

                    var warning = profiles.Count == 0 || folders.Count == 0
                        ? " Warning: Radarr has no " + (profiles.Count == 0 ? "quality profiles" : "root folders") + " configured."
                        : string.Empty;

                    ui.ConnectionStatus.Status = warning.Length > 0 ? ItemStatus.Warning : ItemStatus.Succeeded;
                    ui.ConnectionStatus.StatusText = string.Format(
                        "Connected to {0} {1}. {2} quality profile(s), {3} root folder(s).{4}",
                        status?.AppName ?? "Radarr",
                        status?.Version,
                        profiles.Count,
                        folders.Count,
                        warning);
                }
            }
            catch (RadarrException ex)
            {
                this.SetFailed(ex.Message);
            }
            catch (OperationCanceledException)
            {
                this.SetFailed("Radarr did not answer within 20 seconds.");
            }
            catch (Exception ex)
            {
                this.plugin.Logger.ErrorException("Unexpected error while testing the Radarr connection", ex);
                this.SetFailed("Unexpected error: " + ex.Message);
            }
        }

        private void SetFailed(string message)
        {
            this.Ui.ConnectionStatus.Status = ItemStatus.Failed;
            this.Ui.ConnectionStatus.StatusText = message;
            this.AddSavedValuesAsOptions();
        }

        /// <summary>Keeps the saved choice visible in the dropdown even if Radarr couldn't be reached.</summary>
        private void AddSavedValuesAsOptions()
        {
            var ui = this.Ui;
            if (!string.IsNullOrEmpty(ui.QualityProfileId) && ui.QualityProfileOptions.All(o => o.Value != ui.QualityProfileId))
            {
                ui.QualityProfileOptions.Add(new EditorSelectOption { Value = ui.QualityProfileId, Name = "Profile #" + ui.QualityProfileId + " (saved)", IsEnabled = true });
            }

            if (!string.IsNullOrEmpty(ui.RootFolderPath) && ui.RootFolderOptions.All(o => o.Value != ui.RootFolderPath))
            {
                ui.RootFolderOptions.Add(new EditorSelectOption { Value = ui.RootFolderPath, Name = ui.RootFolderPath + " (saved)", IsEnabled = true });
            }
        }
    }
}
