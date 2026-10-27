using System;
using System.IO;
using SkiaSharp;

namespace EmbyRadarr.Posters
{
    /// <summary>Draws a coloured frame, a status banner and (for downloads) a progress bar on a poster.</summary>
    public static class PosterRenderer
    {
        private static readonly Lazy<SKTypeface> Font = new Lazy<SKTypeface>(() =>
        {
            // Embedded: a NAS often has no fonts installed.
            var stream = typeof(PosterRenderer).Assembly.GetManifestResourceStream("EmbyRadarr.Fonts.Inter-Bold.ttf");
            return stream != null ? SKTypeface.FromStream(stream) : SKTypeface.Default;
        });

        public static SKColor ColorOf(BadgeKind kind)
        {
            switch (kind)
            {
                case BadgeKind.Request: return new SKColor(0x37, 0x41, 0x51);
                case BadgeKind.Requested: return new SKColor(0xE6, 0x9A, 0x16);
                case BadgeKind.Upcoming: return new SKColor(0x8E, 0x44, 0xAD);
                case BadgeKind.Downloading: return new SKColor(0x1E, 0x74, 0xE8);
                case BadgeKind.Available: return new SKColor(0x22, 0x9A, 0x55);
                default: return new SKColor(0xC0, 0x39, 0x2B);
            }
        }

        public static void Render(string inputFile, string outputFile, PosterBadge badge)
        {
            using (var source = SKBitmap.Decode(inputFile))
            {
                if (source == null)
                {
                    throw new InvalidOperationException("Could not decode " + inputFile);
                }

                using (var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height)))
                {
                    Draw(surface.Canvas, source, badge);
                    using (var image = surface.Snapshot())
                    using (var data = Encode(image, outputFile))
                    using (var output = File.Create(outputFile))
                    {
                        data.SaveTo(output);
                    }
                }
            }
        }

        private static void Draw(SKCanvas canvas, SKBitmap poster, PosterBadge badge)
        {
            float w = poster.Width;
            float h = poster.Height;
            var color = ColorOf(badge.Kind);
            canvas.DrawBitmap(poster, 0, 0);

            // Dim movies that can't be watched yet, so they read as "not available" at a glance.
            if (badge.Kind != BadgeKind.Available)
            {
                using (var dim = new SKPaint { Color = new SKColor(0, 0, 0, 70) })
                {
                    canvas.DrawRect(0, 0, w, h, dim);
                }
            }

            var border = Math.Max(4f, w * 0.03f);
            using (var frame = new SKPaint { Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = border, IsAntialias = true })
            {
                canvas.DrawRect(border / 2, border / 2, w - border, h - border, frame);
            }

            // Banner at the top, like the "Blu-Ray" / "4K" banners of cover art plugins.
            var bannerHeight = Math.Max(24f, h * 0.085f);
            using (var fill = new SKPaint { Color = color })
            {
                canvas.DrawRect(0, 0, w, bannerHeight, fill);
            }

            DrawCenteredText(canvas, badge.Text, w, bannerHeight, w - (border * 4));

            if (badge.Kind == BadgeKind.Downloading && badge.Percent.HasValue)
            {
                var barHeight = Math.Max(8f, h * 0.035f);
                var top = h - border - barHeight;
                using (var track = new SKPaint { Color = new SKColor(0, 0, 0, 170) })
                using (var progress = new SKPaint { Color = color })
                {
                    canvas.DrawRect(border, top, w - (border * 2), barHeight, track);
                    canvas.DrawRect(border, top, (w - (border * 2)) * Math.Max(0, Math.Min(100, badge.Percent.Value)) / 100f, barHeight, progress);
                }
            }
        }

        private static void DrawCenteredText(SKCanvas canvas, string text, float width, float bannerHeight, float maxTextWidth)
        {
            using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true, Typeface = Font.Value, TextSize = bannerHeight * 0.52f })
            {
                // Shrink long texts until they fit the banner.
                while (paint.MeasureText(text) > maxTextWidth && paint.TextSize > 8)
                {
                    paint.TextSize -= 1;
                }

                var bounds = default(SKRect);
                paint.MeasureText(text, ref bounds);
                var x = (width - paint.MeasureText(text)) / 2;
                var y = (bannerHeight / 2) - bounds.MidY;
                canvas.DrawText(text, x, y, paint);
            }
        }

        private static SKData Encode(SKImage image, string outputFile)
        {
            var ext = Path.GetExtension(outputFile)?.ToLowerInvariant();
            switch (ext)
            {
                case ".png": return image.Encode(SKEncodedImageFormat.Png, 100);
                case ".webp": return image.Encode(SKEncodedImageFormat.Webp, 90);
                default: return image.Encode(SKEncodedImageFormat.Jpeg, 90);
            }
        }
    }
}
