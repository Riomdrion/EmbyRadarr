using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using EmbyRadarr.Radarr;
using EmbyRadarr.Update;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI.Views;

namespace EmbyRadarr.UI
{
    internal class SettingsPageView : PluginPageView
    {
        private readonly Plugin plugin;

        // Secrets typed in the form but not saved yet. Kept server-side so the form only ever holds the mask.
        private string pendingApiKey;
        private string pendingToken;

        private UpdateInfo availableUpdate;

        public SettingsPageView(PluginInfo pluginInfo, Plugin plugin)
            : base(pluginInfo.Id)
        {
            this.plugin = plugin;
            var config = plugin.Configuration;

            this.ContentData = new SettingsUI
            {
                RadarrUrl = config.RadarrUrl,
                ApiKey = Mask(config.ApiKey),
                QualityProfileId = config.QualityProfileId > 0 ? config.QualityProfileId.ToString() : null,
                RootFolderPath = config.RootFolderPath,
                CacheMinutes = config.CacheMinutesClamped,
                RequestAccess = config.RequestAccess,
                GitHubToken = Mask(config.GitHubToken),
            };
            this.Ui.VersionLabel.Text = "Installed version: " + plugin.Version;
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
            switch (commandId)
            {
                case SettingsUI.TestCommand:
                    await this.TestAndLoad().ConfigureAwait(false);
                    return this;
                case SettingsUI.CheckUpdateCommand:
                    await this.CheckForUpdate().ConfigureAwait(false);
                    return this;
                case SettingsUI.InstallUpdateCommand:
                    await this.InstallUpdate().ConfigureAwait(false);
                    return this;
                case SettingsUI.RestartCommand:
                    this.Restart();
                    return this;
                case SettingsUI.SearchWantedCommand:
                    await this.SearchAllWanted().ConfigureAwait(false);
                    return this;
            }

            return await base.RunCommand(itemId, commandId, data).ConfigureAwait(false);
        }

        public override Task<IPluginUIView> OnSaveCommand(string itemId, string commandId, string data)
        {
            var ui = this.Ui;
            var config = this.plugin.Configuration;

            config.RadarrUrl = (ui.RadarrUrl ?? string.Empty).Trim().TrimEnd('/');
            config.ApiKey = this.ApiKey();
            config.GitHubToken = this.Token();
            config.QualityProfileId = int.TryParse(ui.QualityProfileId, out var id) ? id : 0;
            config.RootFolderPath = ui.RootFolderPath ?? string.Empty;
            config.CacheMinutes = Math.Max(1, Math.Min(5, ui.CacheMinutes));
            config.RequestAccess = ui.RequestAccess;
            this.plugin.SaveConfiguration();
            this.plugin.Data.ClearCache();
            Channel.RadarrChannel.Current?.NotifyChanged();

            this.pendingApiKey = null;
            this.pendingToken = null;
            ui.ApiKey = Mask(config.ApiKey);
            ui.GitHubToken = Mask(config.GitHubToken);
            this.plugin.Logger.Info("Settings saved (Radarr URL: {0})", config.RadarrUrl);

            return base.OnSaveCommand(itemId, commandId, data);
        }

        private static string Mask(string secret) => string.IsNullOrEmpty(secret) ? string.Empty : SettingsUI.ApiKeyMask;

        /// <summary>The real value behind a form field: the mask stands for the pending or saved secret.</summary>
        private static string Resolve(string typed, string pending, string saved)
            => typed?.Trim() == SettingsUI.ApiKeyMask ? (pending ?? saved) : (typed?.Trim() ?? string.Empty);

        private string ApiKey() => Resolve(this.Ui.ApiKey, this.pendingApiKey, this.plugin.Configuration.ApiKey);

        private string Token() => Resolve(this.Ui.GitHubToken, this.pendingToken, this.plugin.Configuration.GitHubToken);

        /// <summary>Remembers newly typed secrets server-side and replaces them with the mask before the form goes back.</summary>
        private void MaskTypedSecrets()
        {
            var ui = this.Ui;
            if (!string.IsNullOrEmpty(ui.ApiKey) && ui.ApiKey.Trim() != SettingsUI.ApiKeyMask)
            {
                this.pendingApiKey = ui.ApiKey.Trim();
                ui.ApiKey = SettingsUI.ApiKeyMask;
            }

            if (!string.IsNullOrEmpty(ui.GitHubToken) && ui.GitHubToken.Trim() != SettingsUI.ApiKeyMask)
            {
                this.pendingToken = ui.GitHubToken.Trim();
                ui.GitHubToken = SettingsUI.ApiKeyMask;
            }
        }

        private async Task TestAndLoad()
        {
            var ui = this.Ui;
            var conn = new RadarrConnection(ui.RadarrUrl, this.ApiKey());
            this.MaskTypedSecrets();
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
            catch (Exception ex)
            {
                SetFailed(ui.ConnectionStatus, ex, "Radarr did not answer within 20 seconds.");
                this.AddSavedValuesAsOptions();
            }
        }

        private async Task CheckForUpdate()
        {
            var ui = this.Ui;
            var token = this.Token();
            this.MaskTypedSecrets();
            ui.InstallUpdateButton.IsVisible = false;
            this.availableUpdate = null;

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var update = await this.plugin.Updater.CheckLatest(this.plugin.Version, token, cts.Token).ConfigureAwait(false);
                    if (update.IsNewer)
                    {
                        this.availableUpdate = update;
                        ui.InstallUpdateButton.IsVisible = true;
                        ui.InstallUpdateButton.Caption = "Install " + update.Tag;
                        ui.UpdateStatus.Status = ItemStatus.Warning;
                        ui.UpdateStatus.StatusText = update.Tag + " is available (installed: " + this.plugin.Version + ").";
                    }
                    else
                    {
                        ui.UpdateStatus.Status = ItemStatus.Succeeded;
                        ui.UpdateStatus.StatusText = "Up to date (latest release: " + update.Tag + ").";
                    }
                }
            }
            catch (Exception ex)
            {
                SetFailed(ui.UpdateStatus, ex, "GitHub did not answer within 30 seconds.");
            }
        }

        private async Task InstallUpdate()
        {
            var ui = this.Ui;
            var update = this.availableUpdate;
            if (update == null)
            {
                ui.UpdateStatus.Status = ItemStatus.Warning;
                ui.UpdateStatus.StatusText = "Click 'Check for updates' first.";
                return;
            }

            var token = this.Token();
            this.MaskTypedSecrets();

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
                {
                    await this.plugin.Updater.Install(update, token, this.plugin.AssemblyFilePath, cts.Token).ConfigureAwait(false);
                }

                this.availableUpdate = null;
                ui.InstallUpdateButton.IsVisible = false;
                this.plugin.AppHost.NotifyPendingRestart();

                var canRestart = this.plugin.AppHost.CanSelfRestart;
                ui.RestartButton.IsVisible = canRestart;
                ui.UpdateStatus.Status = ItemStatus.Succeeded;
                ui.UpdateStatus.StatusText = "Installed " + update.Tag + ". Restart Emby to activate it"
                    + (canRestart ? "." : " (via your NAS: Package Center > Emby Server > Stop, then Run).");
            }
            catch (Exception ex)
            {
                SetFailed(ui.UpdateStatus, ex, "The download took too long.");
            }
        }

        private async Task SearchAllWanted()
        {
            var status = this.Ui.SearchStatus;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    await this.plugin.Data.SearchAllWanted(cts.Token).ConfigureAwait(false);
                }

                status.Status = ItemStatus.Succeeded;
                status.StatusText = "Radarr is searching for all requested movies. Progress shows under Downloading.";
            }
            catch (Exception ex)
            {
                SetFailed(status, ex, "Radarr did not answer within 20 seconds.");
            }
        }

        private void Restart()
        {
            this.Ui.RestartButton.IsEnabled = false;
            this.Ui.UpdateStatus.Status = ItemStatus.InProgress;
            this.Ui.UpdateStatus.StatusText = "Emby is restarting. Reload the page in a minute.";
            this.plugin.Logger.Info("Restart requested from the {0} settings page", Plugin.PluginName);

            // Let this response reach the browser before the server goes down.
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                this.plugin.AppHost.Restart();
            });
        }

        private void SetFailed(StatusItem status, Exception ex, string timeoutMessage)
        {
            status.Status = ItemStatus.Failed;
            switch (ex)
            {
                case RadarrException known:
                    status.StatusText = known.Message;
                    break;
                case OperationCanceledException _:
                    status.StatusText = timeoutMessage;
                    break;
                default:
                    this.plugin.Logger.ErrorException("Unexpected error on the settings page", ex);
                    status.StatusText = "Unexpected error: " + ex.Message;
                    break;
            }
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
