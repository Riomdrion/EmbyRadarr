using System;
using System.Collections.Generic;
using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Validation;
using MediaBrowser.Model.Attributes;

namespace EmbyRadarr.UI
{
    /// <summary>The settings form. Emby renders it from these properties; no HTML/JS needed.</summary>
    public class SettingsUI : EditableOptionsBase
    {
        public const string TestCommand = "TestConnection";

        /// <summary>Shown instead of the real API key so the key is never sent to the browser.</summary>
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
