using System;
using System.Collections.Generic;
using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Validation;
using EmbyRadarr.Configuration;
using MediaBrowser.Model.Attributes;

namespace EmbyRadarr.UI
{
    /// <summary>The settings form. Emby renders it from these properties; no HTML/JS needed.</summary>
    public class SettingsUI : EditableOptionsBase
    {
        public const string TestCommand = "TestConnection";
        public const string CheckUpdateCommand = "CheckUpdate";
        public const string InstallUpdateCommand = "InstallUpdate";
        public const string RestartCommand = "RestartEmby";
        public const string SearchWantedCommand = "SearchWanted";

        /// <summary>Shown instead of a saved secret (API key, token) so it is never sent to the browser.</summary>
        public const string ApiKeyMask = "********";

        public override string EditorTitle => "Radarr";

        public override string EditorDescription => "Connect Emby to Radarr. Wanted, upcoming and downloading movies appear in the Radarr channel.";

        public CaptionItem ConnectionCaption { get; set; } = new CaptionItem("Connection");

        [DisplayName("Radarr URL")]
        [Description("For example http://192.168.1.10:7878. Include the URL base if you set one in Radarr, e.g. http://host:7878/radarr")]
        [Required]
        public string RadarrUrl { get; set; }

        [DisplayName("API key")]
        [Description("Radarr > Settings > General > Security > API Key")]
        [IsPassword]
        public string ApiKey { get; set; }

        public ButtonItem TestButton { get; set; } = new ButtonItem("Test connection") { Icon = IconNames.run_circle, Data1 = TestCommand };

        public StatusItem ConnectionStatus { get; set; } = new StatusItem("Status", "Not tested yet", ItemStatus.Unavailable);

        public SpacerItem Spacer1 { get; set; } = new SpacerItem();

        public CaptionItem AddCaption { get; set; } = new CaptionItem("Defaults for adding movies");

        public LabelItem AddHint { get; set; } = new LabelItem("These lists are loaded from Radarr. Run 'Test connection' after changing the URL or API key to refresh them.");

        [Browsable(false)]
        public List<EditorSelectOption> QualityProfileOptions { get; set; } = new List<EditorSelectOption>();

        [Browsable(false)]
        public List<EditorSelectOption> RootFolderOptions { get; set; } = new List<EditorSelectOption>();

        [DisplayName("Quality profile")]
        [SelectItemsSource(nameof(QualityProfileOptions))]
        public string QualityProfileId { get; set; }

        [DisplayName("Root folder")]
        [SelectItemsSource(nameof(RootFolderOptions))]
        public string RootFolderPath { get; set; }

        public SpacerItem Spacer2 { get; set; } = new SpacerItem();

        public CaptionItem ChannelCaption { get; set; } = new CaptionItem("Channel and requests");

        public LabelItem RequestHint { get; set; } = new LabelItem("Users see the channel as \"Films aanvragen\" (Dutch, no mention of Radarr). "
            + "They request a movie by tapping the heart (favorite) on it under Ontdekken or Zoeken, and search by creating a playlist named \"Zoek: title\". "
            + "A heart on a released movie under Aangevraagd makes Radarr search for it again.");

        [DisplayName("Who can request movies")]
        public RequestAccess RequestAccess { get; set; } = RequestAccess.Everyone;

        public ButtonItem SearchWantedButton { get; set; } = new ButtonItem("Search all requested movies now") { Icon = IconNames.search, Data1 = SearchWantedCommand };

        public StatusItem SearchStatus { get; set; } = new StatusItem("Search", "Radarr also searches on its own schedule.", ItemStatus.None);

        [DisplayName("Cache duration (minutes)")]
        [Description("How long Radarr data is reused before asking Radarr again (1-5).")]
        [MinValue(1)]
        [MaxValue(5)]
        public int CacheMinutes { get; set; } = 2;

        public SpacerItem Spacer3 { get; set; } = new SpacerItem();

        public CaptionItem UpdateCaption { get; set; } = new CaptionItem("Plugin updates");

        public LabelItem VersionLabel { get; set; } = new LabelItem(string.Empty);

        [DisplayName("GitHub token")]
        [Description("Only needed while the GitHub repository is private: a fine-grained token with read-only 'Contents' access to that repository.")]
        [IsPassword]
        public string GitHubToken { get; set; }

        public ButtonItem CheckUpdateButton { get; set; } = new ButtonItem("Check for updates") { Icon = IconNames.refresh, Data1 = CheckUpdateCommand };

        public StatusItem UpdateStatus { get; set; } = new StatusItem("Update", "Not checked yet", ItemStatus.Unavailable);

        public ButtonItem InstallUpdateButton { get; set; } = new ButtonItem("Install update") { Icon = IconNames.system_update, Data1 = InstallUpdateCommand, IsVisible = false };

        public ButtonItem RestartButton { get; set; } = new ButtonItem("Restart Emby now")
        {
            Icon = IconNames.restart_alt,
            Data1 = RestartCommand,
            IsVisible = false,
            ConfirmationPrompt = "Restart Emby now? Anything that is playing will stop.",
        };

        protected override void Validate(ValidationContext context)
        {
            var url = this.RadarrUrl?.Trim();
            if (!string.IsNullOrEmpty(url)
                && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")))
            {
                context.AddValidationError(nameof(this.RadarrUrl), "Must start with http:// or https://");
            }
        }
    }
}
