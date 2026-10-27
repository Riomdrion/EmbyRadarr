using EmbyRadarr.Channel;
using EmbyRadarr.Posters;
using EmbyRadarr.Radarr;
using SkiaSharp;

namespace EmbyRadarr.Tests;

public class PosterTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("posters-").FullName;

    public void Dispose() => Directory.Delete(this.dir, true);

    /// <summary>A plain 600x900 test poster: blue-to-orange gradient.</summary>
    internal static string MakePoster(string dir, string name = "in.jpg")
    {
        var path = Path.Combine(dir, name);
        using var surface = SKSurface.Create(new SKImageInfo(600, 900));
        using (var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 900), new[] { new SKColor(20, 40, 90), new SKColor(230, 140, 40) }, SKShaderTileMode.Clamp),
        })
        {
            surface.Canvas.DrawRect(0, 0, 600, 900, paint);
        }

        using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Jpeg, 95);
        using var file = File.Create(path);
        data.SaveTo(file);
        return path;
    }

    [Theory]
    [InlineData(BadgeKind.Request, "AANVRAGEN", null)]
    [InlineData(BadgeKind.Requested, "AANGEVRAAGD", null)]
    [InlineData(BadgeKind.Upcoming, "VERWACHT 14 NOV", null)]
    [InlineData(BadgeKind.Downloading, "DOWNLOADEN 45%", 45)]
    [InlineData(BadgeKind.Available, "AL TE KIJKEN", null)]
    [InlineData(BadgeKind.Failed, "MISLUKT", null)]
    public void Draws_banner_frame_and_keeps_size(BadgeKind kind, string text, int? percent)
    {
        var input = MakePoster(this.dir);
        var output = Path.Combine(this.dir, "out.jpg");

        PosterRenderer.Render(input, output, new PosterBadge(kind, text, percent));

        using var result = SKBitmap.Decode(output);
        Assert.Equal((600, 900), (result.Width, result.Height));
        var expected = PosterRenderer.ColorOf(kind);
        AssertClose(expected, result.GetPixel(3, 450));   // left frame
        AssertClose(expected, result.GetPixel(10, 5));    // banner, left of the text
    }

    [Fact]
    public void Download_progress_bar_is_filled_up_to_the_percentage()
    {
        var input = MakePoster(this.dir);
        var output = Path.Combine(this.dir, "out.png");

        PosterRenderer.Render(input, output, new PosterBadge(BadgeKind.Downloading, "DOWNLOADEN 45%", 45));

        using var result = SKBitmap.Decode(output);
        var barY = 900 - 18 - 10;
        AssertClose(PosterRenderer.ColorOf(BadgeKind.Downloading), result.GetPixel(200, barY));
        Assert.True(result.GetPixel(400, barY).Blue < 120, "right of 45% should be the dark track");
    }

    [Fact]
    public void Channel_items_register_the_right_badge()
    {
        var queued = ItemBuilder.Queued(new QueueItem { Id = 77, Title = "X", Status = "downloading", Size = 100, SizeLeft = 55 });
        var wanted = ItemBuilder.Wanted(new Movie { Id = 5, Title = "Y", Monitored = true });
        var request = ItemBuilder.Discover(new Movie { TmdbId = 9, Title = "Z" }, null, ItemBuilder.PopularFolderId);
        var upcoming = ItemBuilder.Upcoming(new Movie { Id = 6, Title = "U", DigitalRelease = "2026-11-14T00:00:00Z" }, new DateTime(2026, 10, 27));

        Assert.Equal(("DOWNLOADEN 45%", 45), (PosterBadges.Get(queued.Id)!.Text, PosterBadges.Get(queued.Id)!.Percent!.Value));
        Assert.Equal(BadgeKind.Requested, PosterBadges.Get(wanted.Id)!.Kind);
        Assert.Equal(BadgeKind.Request, PosterBadges.Get(request.Id)!.Kind);
        Assert.Equal("VERWACHT 14 NOV", PosterBadges.Get(upcoming.Id)!.Text);
        Assert.Null(PosterBadges.Get("library-item"));
    }

    [Fact]
    public void Cache_key_changes_in_5_percent_steps()
    {
        Assert.Equal(new PosterBadge(BadgeKind.Downloading, "A", 41).CacheKey.Split('|')[2], new PosterBadge(BadgeKind.Downloading, "A", 44).CacheKey.Split('|')[2]);
        Assert.NotEqual(new PosterBadge(BadgeKind.Downloading, "A", 44).CacheKey, new PosterBadge(BadgeKind.Downloading, "B", 45).CacheKey);
    }

    [Fact]
    public void Broken_input_falls_back_to_the_original_poster()
    {
        var input = Path.Combine(this.dir, "broken.jpg");
        File.WriteAllText(input, "not an image");
        var output = Path.Combine(this.dir, "out.jpg");
        var item = new MediaBrowser.Controller.Entities.Movies.Movie { ExternalId = ItemBuilder.Wanted(new Movie { Id = 99, Title = "B" }).Id };

        new StatusPosterEnhancer().EnhanceImageAsync(item, input, output, MediaBrowser.Model.Entities.ImageType.Primary, 0).Wait();

        Assert.Equal("not an image", File.ReadAllText(output));
    }

    private static void AssertClose(SKColor expected, SKColor actual)
        => Assert.True(Math.Abs(expected.Red - actual.Red) < 24 && Math.Abs(expected.Green - actual.Green) < 24 && Math.Abs(expected.Blue - actual.Blue) < 24, $"expected {expected}, got {actual}");
}
