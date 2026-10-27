using System;
using System.IO;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;

namespace EmbyRadarr.Posters
{
    /// <summary>
    /// Adds a status frame and banner ("AANGEVRAAGD", "DOWNLOADEN 45%", ...) to the posters of this plugin's channel items.
    /// IImageEnhancer is documented SDK API; the drawing uses the SkiaSharp library that Emby ships (undocumented).
    /// If drawing fails, the original poster is used.
    /// </summary>
    public class StatusPosterEnhancer : IImageEnhancer
    {
        private const string StyleVersion = "rb1";
        private static bool warned;

        public MetadataProviderPriority Priority => MetadataProviderPriority.Last;

        public bool Supports(BaseItem item, ImageType imageType)
            => imageType == ImageType.Primary
               && Plugin.Instance?.Configuration.PosterBadges == true
               && PosterBadges.Get(item?.ExternalId) != null;

        public string GetConfigurationCacheKey(BaseItem item, ImageType imageType)
            => StyleVersion + "|" + (PosterBadges.Get(item?.ExternalId)?.CacheKey ?? "none");

        public ImageSize GetEnhancedImageSize(BaseItem item, ImageType imageType, int imageIndex, ImageSize originalImageSize)
            => originalImageSize;

        public EnhancedImageInfo GetEnhancedImageInfo(BaseItem item, string inputFile, ImageType imageType, int imageIndex)
            => new EnhancedImageInfo { RequiresTransparency = false };

        public Task EnhanceImageAsync(BaseItem item, string inputFile, string outputFile, ImageType imageType, int imageIndex)
        {
            var badge = PosterBadges.Get(item?.ExternalId);
            try
            {
                if (badge == null)
                {
                    File.Copy(inputFile, outputFile, true);
                }
                else
                {
                    PosterRenderer.Render(inputFile, outputFile, badge);
                }
            }
            catch (Exception ex)
            {
                if (!warned)
                {
                    warned = true;
                    Plugin.Instance?.Logger.ErrorException("Could not draw status banners on posters; showing plain posters", ex);
                }

                File.Copy(inputFile, outputFile, true);
            }

            return Task.CompletedTask;
        }
    }
}
