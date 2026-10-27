using System.Net;
using EmbyRadarr.Configuration;
using EmbyRadarr.Radarr;
using EmbyRadarr.Requests;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Users;

namespace EmbyRadarr.Tests;

public class RadarrServiceTests
{
    private readonly FakeRadarr radarr = new();
    private readonly PluginConfiguration config = new()
    {
        RadarrUrl = "http://radarr:7878",
        ApiKey = "key",
        QualityProfileId = 4,
        RootFolderPath = "/movies",
    };

    private readonly RadarrService service;
    private int changed;

    public RadarrServiceTests()
    {
        this.service = new RadarrService(new RadarrClient(new RecordingLogger(), this.radarr), () => this.config);
        this.service.Changed += (_, _) => this.changed++;
    }

    [Fact]
    public async Task Request_adds_the_movie_with_configured_defaults_and_starts_a_search()
    {
        this.radarr.On("GET /api/v3/movie", "[]");
        this.radarr.On("GET /api/v3/movie/lookup/tmdb", """
            {"title":"Dune","year":2021,"tmdbId":438631,"images":[{"coverType":"poster","url":"/x.jpg","remoteUrl":"https://image.tmdb.org/x.jpg"}]}
            """);
        this.radarr.On("POST /api/v3/movie", """{"id":12,"title":"Dune","tmdbId":438631}""", HttpStatusCode.Created);

        var result = await this.service.RequestMovie(438631, default);

        Assert.Equal("Requested Dune (2021).", result);
        var post = this.radarr.Requests.Single(r => r.Request.Method == HttpMethod.Post);
        Assert.Contains("\"tmdbId\":438631", post.Body);
        Assert.Contains("\"qualityProfileId\":4", post.Body);
        Assert.Contains("\"rootFolderPath\":\"\\/movies\"", post.Body);
        Assert.Contains("\"monitored\":true", post.Body);
        Assert.Contains("\"addOptions\":{\"searchForMovie\":true}", post.Body);
        Assert.Contains("\"remoteUrl\":\"https:\\/\\/image.tmdb.org\\/x.jpg\"", post.Body);
        Assert.Equal("tmdbId=438631", this.radarr.Requests.Single(r => r.Request.RequestUri!.AbsolutePath.EndsWith("/tmdb")).Request.RequestUri!.Query.TrimStart('?'));
        Assert.Equal(1, this.changed);
    }

    [Fact]
    public async Task Request_for_a_movie_already_in_radarr_does_not_post()
    {
        this.radarr.On("GET /api/v3/movie", """[{"id":12,"title":"Dune","tmdbId":438631}]""");

        var result = await this.service.RequestMovie(438631, default);

        Assert.Equal("Dune is already in Radarr.", result);
        Assert.DoesNotContain(this.radarr.Requests, r => r.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Request_without_defaults_fails_clearly_and_touches_nothing()
    {
        this.config.QualityProfileId = 0;

        var ex = await Assert.ThrowsAsync<RadarrException>(() => this.service.RequestMovie(438631, default));

        Assert.Contains("quality profile and root folder", ex.Message);
        Assert.Empty(this.radarr.Requests);
    }

    [Fact]
    public async Task Search_commands_use_radarr_command_names()
    {
        this.radarr.On("POST /api/v3/command", """{"id":1,"status":"queued"}""", HttpStatusCode.Created);

        await this.service.SearchMovie(12, default);
        await this.service.SearchAllWanted(default);

        var bodies = this.radarr.Requests.Select(r => r.Body).ToList();
        Assert.Equal("""{"movieIds":[12],"name":"MoviesSearch"}""", bodies[0]);
        Assert.Equal("""{"name":"MissingMoviesSearch"}""", bodies[1]);
        Assert.Equal(2, this.changed);
    }

    [Fact]
    public async Task Discover_parses_flags_and_drops_excluded_movies()
    {
        this.radarr.On("GET /api/v3/importlist/movie", """
            [{"title":"A","tmdbId":1,"isTrending":true,"isExisting":false},
             {"title":"B","tmdbId":2,"isPopular":true,"isExcluded":true},
             {"title":"C","tmdbId":3,"isRecommendation":true,"isExisting":true}]
            """);

        var movies = await this.service.GetDiscover(default);

        Assert.Equal(new[] { "A", "C" }, movies.Select(m => m.Title));
        Assert.True(movies[0].IsTrending);
        Assert.True(movies[1].IsRecommendation);
        Assert.Equal("/api/v3/importlist/movie?includeRecommendations=true&includeTrending=true&includePopular=true", this.radarr.Requests.Single().Request.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Search_looks_up_the_term_and_keeps_recent_searches_newest_first()
    {
        this.radarr.On("GET /api/v3/movie/lookup", """[{"title":"Dune","year":2021,"tmdbId":438631},{"title":"Dune","year":1984,"tmdbId":841},{"title":"No id","tmdbId":0}]""");

        var first = await this.service.Search("dune & co", default);
        var second = await this.service.Search("dune", default);

        Assert.Equal("/api/v3/movie/lookup?term=dune%20%26%20co", this.radarr.Requests[0].Request.RequestUri!.PathAndQuery);
        Assert.Equal(new[] { 2021, 1984 }, first.Movies.Select(m => m.Year));
        Assert.Equal(new[] { second.Id, first.Id }, this.service.Searches.Select(s => s.Id));
        Assert.Same(first, this.service.GetSearch(first.Id));
        Assert.Equal(2, this.changed);
    }

    [Fact]
    public async Task Only_the_last_15_searches_are_kept()
    {
        this.radarr.On("GET /api/v3/movie/lookup", "[]");

        for (var i = 0; i < 20; i++)
        {
            await this.service.Search("film " + i, default);
        }

        Assert.Equal(RadarrService.MaxSearches, this.service.Searches.Count);
        Assert.Equal("film 19", this.service.Searches[0].Term);
    }

    [Theory]
    [InlineData("Zoek: Dune", "Dune")]
    [InlineData("  zoek:dune 2021 ", "dune 2021")]
    [InlineData("ZOEKEN: The Matrix", "The Matrix")]
    [InlineData("zoek Dune", "Dune")]
    [InlineData("Search: Dune", "Dune")]
    [InlineData("Zoek: D", null)]
    [InlineData("Mijn favorieten", null)]
    [InlineData("\u2713 3 films gevonden voor \u201cDune\u201d", null)]
    [InlineData(null, null)]
    public void Playlist_names_that_are_searches(string? name, string? term)
        => Assert.Equal(term, PlaylistSearchHandler.ParseSearchTerm(name!));

    [Fact]
    public void Search_result_ids_are_handled_like_discover_by_the_heart()
    {
        var movie = new MediaBrowser.Controller.Entities.Movies.Movie { ExternalId = EmbyRadarr.Channel.ItemBuilder.SearchFolderPrefix + "3:438631" };

        Assert.Equal("request:438631", FavoriteRequestHandler.ParseAction(movie)!.ToString());
    }

    [Theory]
    [InlineData("request:603", "request", 603)]
    [InlineData("search:12", "search", 12)]
    [InlineData("delete:12", null, 0)]
    [InlineData("request:-1", null, 0)]
    [InlineData("request:abc", null, 0)]
    [InlineData("", null, 0)]
    public void Favorite_actions_are_parsed_strictly(string value, string? kind, int id)
    {
        var action = FavoriteAction.Parse(value);

        Assert.Equal(kind, action?.Kind);
        Assert.Equal(id, action?.Id ?? 0);
    }

    [Fact]
    public void Action_comes_from_provider_id_or_falls_back_to_the_channel_item_id()
    {
        var withProviderId = new MediaBrowser.Controller.Entities.Movies.Movie();
        withProviderId.ProviderIds[EmbyRadarr.Channel.ItemBuilder.ActionKey] = "search:12";
        var withExternalId = new MediaBrowser.Controller.Entities.Movies.Movie { ExternalId = "discover-popular:603" };
        var libraryMovie = new MediaBrowser.Controller.Entities.Movies.Movie { ExternalId = "wanted:12" };

        Assert.Equal("search:12", FavoriteRequestHandler.ParseAction(withProviderId)!.ToString());
        Assert.Equal("request:603", FavoriteRequestHandler.ParseAction(withExternalId)!.ToString());
        Assert.Null(FavoriteRequestHandler.ParseAction(libraryMovie));
        Assert.Null(FavoriteRequestHandler.ParseAction(new MediaBrowser.Controller.Entities.Movies.Movie()));
    }

    [Fact]
    public void Request_access_respects_the_setting()
    {
        var admin = new User { Policy = new UserPolicy { IsAdministrator = true } };
        var regular = new User { Policy = new UserPolicy { IsAdministrator = false } };

        Assert.True(FavoriteRequestHandler.MayRequest(this.config, regular));
        this.config.RequestAccess = RequestAccess.AdministratorsOnly;
        Assert.False(FavoriteRequestHandler.MayRequest(this.config, regular));
        Assert.True(FavoriteRequestHandler.MayRequest(this.config, admin));
    }
}
