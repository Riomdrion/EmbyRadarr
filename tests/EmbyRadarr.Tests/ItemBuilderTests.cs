using EmbyRadarr.Channel;
using EmbyRadarr.Radarr;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

namespace EmbyRadarr.Tests;

public class ItemBuilderTests
{
    private static readonly DateTime Today = new(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);

    private static Movie MakeMovie(int id = 1, string title = "Dune", bool monitored = true, bool hasFile = false, bool available = true) => new()
    {
        Id = id,
        Title = title,
        Year = 2026,
        Monitored = monitored,
        HasFile = hasFile,
        IsAvailable = available,
        Overview = "Sand.",
        TmdbId = 438631,
        ImdbId = "tt1160419",
        Runtime = 155,
        Added = "2026-01-02T03:04:05Z",
        InCinemas = "2026-12-18T00:00:00Z",
        DigitalRelease = "2026-11-14T00:00:00Z",
        Images = new() { new() { CoverType = "fanart", RemoteUrl = "https://image.tmdb.org/fanart.jpg" }, new() { CoverType = "poster", RemoteUrl = "https://image.tmdb.org/poster.jpg" } },
    };

    [Fact]
    public void Wanted_means_monitored_without_file()
    {
        var movies = new[] { MakeMovie(1), MakeMovie(2, monitored: false), MakeMovie(3, hasFile: true) };

        Assert.Equal(new[] { 1 }, ItemBuilder.SelectWanted(movies).Select(m => m.Id));
    }

    [Fact]
    public void Wanted_item_has_title_year_poster_status_and_ids()
    {
        var item = ItemBuilder.Wanted(MakeMovie());

        Assert.Equal("wanted:1", item.Id);
        Assert.Equal("Dune \u00b7 Nog niet te kijken", item.Name);
        Assert.Equal(2026, item.ProductionYear);
        Assert.Equal("https://image.tmdb.org/poster.jpg", item.ImageUrl);
        Assert.StartsWith("Aangevraagd, maar nog niet te kijken.", item.Overview);
        Assert.EndsWith("\n\nSand.", item.Overview);
        Assert.Equal("search:1", item.ProviderIds[ItemBuilder.ActionKey]);
        Assert.Equal("438631", item.ProviderIds["Tmdb"]);
        Assert.Equal("tt1160419", item.ProviderIds["Imdb"]);
        Assert.Equal(TimeSpan.FromMinutes(155).Ticks, item.RunTimeTicks);
        Assert.Equal(ChannelItemType.Media, item.Type);
        Assert.Equal(ChannelMediaContentType.Movie, item.ContentType);
        Assert.Contains("Aangevraagd", item.Tags);
    }

    [Fact]
    public void Unreleased_wanted_movie_says_so()
    {
        var item = ItemBuilder.Wanted(MakeMovie(available: false));

        Assert.StartsWith("Aangevraagd. De film is nog niet uitgekomen.", item.Overview);
        Assert.False(item.ProviderIds.ContainsKey(ItemBuilder.ActionKey));
    }

    [Fact]
    public void Remote_poster_field_wins_and_local_mediacover_urls_are_never_used()
    {
        var m = MakeMovie();
        m.RemotePoster = "https://image.tmdb.org/remote.jpg";
        Assert.Equal("https://image.tmdb.org/remote.jpg", ItemBuilder.PosterUrl(m));

        var local = new Movie { Images = new() { new() { CoverType = "poster", RemoteUrl = null } } };
        Assert.Null(ItemBuilder.PosterUrl(local));
    }

    [Fact]
    public void Trailer_becomes_external_id_and_link_in_overview()
    {
        var m = MakeMovie();
        m.YouTubeTrailerId = "Way9Dexny3w";

        var item = ItemBuilder.Wanted(m);

        Assert.Equal("Way9Dexny3w", item.ProviderIds[TrailerExternalId.ProviderKey]);
        Assert.Contains("Trailer: https://www.youtube.com/watch?v=Way9Dexny3w", item.Overview);
        Assert.True(new TrailerExternalId().Supports(item));
        Assert.False(new TrailerExternalId().Supports(ItemBuilder.Wanted(MakeMovie())));
    }

    [Fact]
    public void Upcoming_shows_the_next_release_on_or_after_today()
    {
        var item = ItemBuilder.Upcoming(MakeMovie(), Today);

        Assert.StartsWith("Komt uit op 14 november 2026 (digitaal). Nog niet te kijken.", item.Overview);
        Assert.Equal("Dune \u00b7 Verwacht 14 nov", item.Name);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero), item.PremiereDate);
    }

    [Fact]
    public void Upcoming_skips_releases_in_the_past()
    {
        var m = MakeMovie();
        m.InCinemas = "2026-09-01T00:00:00Z";
        m.DigitalRelease = null;
        m.PhysicalRelease = "2026-12-01T00:00:00Z";

        Assert.StartsWith("Komt uit op 1 december 2026 (op dvd/blu-ray)", ItemBuilder.Upcoming(m, Today).Overview);
    }

    [Theory]
    [InlineData("downloading", "downloading", 1000, 550, "00:12:00", "Wordt gedownload 45%, nog 12 min")]
    [InlineData("downloading", "downloading", 1000, 0, "01:30:00", "Wordt gedownload 100%, nog 1 u 30 min")]
    [InlineData("downloading", "downloading", 0, 0, null, "Wordt gedownload 0%")]
    [InlineData("paused", "downloading", 1000, 900, null, "Gepauzeerd 10%")]
    [InlineData("queued", "downloading", 1000, 1000, null, "In de wachtrij")]
    [InlineData("completed", "importPending", 1000, 0, null, "Bijna klaar")]
    [InlineData("completed", "downloading", 1000, 0, null, "Bijna klaar")]
    [InlineData("failed", "failed", 1000, 0, null, "Mislukt")]
    public void Queue_status_text(string status, string state, double size, double left, string? timeLeft, string expected)
    {
        var q = new QueueItem { Status = status, TrackedDownloadState = state, Size = size, SizeLeft = left, TimeLeft = timeLeft, ErrorMessage = status == "failed" ? "disk full" : null };

        Assert.Equal(expected, ItemBuilder.QueueStatus(q));
    }

    [Fact]
    public void Queue_item_without_embedded_movie_still_renders()
    {
        var item = ItemBuilder.Queued(new QueueItem { Id = 9, MovieId = 3, Title = "Some.Release.2160p", Status = "downloading", Size = 10, SizeLeft = 5 });

        Assert.Equal("queue:9", item.Id);
        Assert.Equal("Some.Release.2160p \u00b7 Wordt gedownload 50%", item.Name);
        Assert.StartsWith("Wordt gedownload 50%", item.Overview);
    }

    [Fact]
    public void Default_sort_is_soonest_release_and_supports_emby_sort_and_paging()
    {
        var a = ItemBuilder.Wanted(MakeMovie(1, "B-movie"));
        var b = ItemBuilder.Wanted(MakeMovie(2, "A-movie"));
        b.PremiereDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var items = new List<ChannelItemInfo> { a, b };

        var byDefault = ItemBuilder.Page(items, new InternalChannelItemQuery(), ItemBuilder.WantedFolderId);
        Assert.Equal(new[] { "A-movie", "B-movie" }, byDefault.Items.Select(Title));

        var byNameDesc = ItemBuilder.Page(items, new InternalChannelItemQuery { SortBy = ChannelItemSortField.Name, SortDescending = true }, ItemBuilder.WantedFolderId);
        Assert.Equal(new[] { "B-movie", "A-movie" }, byNameDesc.Items.Select(Title));

        var paged = ItemBuilder.Page(items, new InternalChannelItemQuery { StartIndex = 1, Limit = 1 }, ItemBuilder.WantedFolderId);
        Assert.Single(paged.Items);
        Assert.Equal(2, paged.TotalRecordCount);
    }

    private static string Title(ChannelItemInfo i) => i.Name.Split(" \u00b7 ")[0];

    [Fact]
    public void Discover_item_not_in_radarr_can_be_requested_with_the_heart()
    {
        var item = ItemBuilder.Discover(MakeMovie(), null, ItemBuilder.TrendingFolderId);

        Assert.Equal("Dune \u00b7 Tik \u2665 om aan te vragen", item.Name);
        Assert.Equal("request:438631", item.ProviderIds[ItemBuilder.ActionKey]);
        Assert.Equal("discover-trending:438631", item.Id);
        Assert.StartsWith("Nog niet beschikbaar.", item.Overview);
    }

    [Fact]
    public void Discover_item_already_in_radarr_shows_requested_or_in_library_and_has_no_action()
    {
        var requested = ItemBuilder.Discover(MakeMovie(), MakeMovie(hasFile: false), ItemBuilder.PopularFolderId);
        var inLibrary = ItemBuilder.Discover(MakeMovie(), MakeMovie(hasFile: true), ItemBuilder.PopularFolderId);

        Assert.Equal("Dune \u00b7 Aangevraagd", requested.Name);
        Assert.Equal("Dune \u00b7 Al te kijken", inLibrary.Name);
        Assert.False(requested.ProviderIds.ContainsKey(ItemBuilder.ActionKey));
        Assert.False(inLibrary.ProviderIds.ContainsKey(ItemBuilder.ActionKey));
    }

    [Fact]
    public void Message_items_are_folders_so_they_cannot_be_played()
    {
        var msg = ItemBuilder.Message("error", "Radarr unavailable", "Could not reach Radarr");

        Assert.Equal(ChannelItemType.Folder, msg.Type);
        Assert.Equal("Could not reach Radarr", msg.Overview);
    }
}
