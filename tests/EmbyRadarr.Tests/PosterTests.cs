using EmbyRadarr.Channel;
using EmbyRadarr.Posters;
using EmbyRadarr.Radarr;

namespace EmbyRadarr.Tests;

/// <summary>The channel marks items for the separate Poster Banners plugin; it draws nothing itself.</summary>
public class PosterTests
{
    private static string Banner(MediaBrowser.Controller.Channels.ChannelItemInfo item) => item.ProviderIds[PosterBadge.ProviderKey];

    [Fact]
    public void Channel_items_carry_the_poster_banner_marker()
    {
        var queued = ItemBuilder.Queued(new QueueItem { Id = 77, Title = "X", Status = "downloading", Size = 100, SizeLeft = 55 });
        var wanted = ItemBuilder.Wanted(new Movie { Id = 5, Title = "Y", Monitored = true });
        var request = ItemBuilder.Discover(new Movie { TmdbId = 9, Title = "Z" }, null, ItemBuilder.PopularFolderId);
        var inLibrary = ItemBuilder.Discover(new Movie { TmdbId = 9, Title = "Z" }, new Movie { HasFile = true }, ItemBuilder.PopularFolderId);
        var upcoming = ItemBuilder.Upcoming(new Movie { Id = 6, Title = "U", DigitalRelease = "2026-11-14T00:00:00Z" }, new DateTime(2026, 10, 27));
        var failed = ItemBuilder.Queued(new QueueItem { Id = 78, Title = "F", Status = "failed", TrackedDownloadState = "failed" });

        Assert.Equal("Downloading|45|DOWNLOADEN 45%", Banner(queued));
        Assert.Equal("Requested||AANGEVRAAGD", Banner(wanted));
        Assert.Equal("Request||AANVRAGEN", Banner(request));
        Assert.Equal("Available||AL TE KIJKEN", Banner(inLibrary));
        Assert.Equal("Upcoming||VERWACHT 14 NOV", Banner(upcoming));
        Assert.Equal("Failed||MISLUKT", Banner(failed));
    }

    [Fact]
    public void Marker_key_matches_the_poster_banners_plugin() => Assert.Equal("PosterBanner", PosterBadge.ProviderKey);

    [Fact]
    public void Items_lock_their_text_against_metadata_refresh()
    {
        var item = ItemBuilder.Wanted(new Movie { Id = 1, Title = "L" });

        Assert.Contains(MediaBrowser.Model.Entities.MetadataFields.Name, item.LockedFields);
        Assert.Contains(MediaBrowser.Model.Entities.MetadataFields.Overview, item.LockedFields);
    }

    [Fact]
    public void Only_changed_items_are_force_updated()
    {
        MediaBrowser.Controller.Channels.ChannelItemInfo Q(double left) => ItemBuilder.Queued(new QueueItem { Id = 500, Title = "Q", Status = "downloading", Size = 100, SizeLeft = left });

        var first = Q(80);
        ChangeTracker.MarkChanged(new[] { first });
        var same = Q(80);
        ChangeTracker.MarkChanged(new[] { same });
        var progressed = Q(50);
        ChangeTracker.MarkChanged(new[] { progressed });

        Assert.True(first.ForceUpdate);
        Assert.False(same.ForceUpdate);
        Assert.True(progressed.ForceUpdate);
    }
}
