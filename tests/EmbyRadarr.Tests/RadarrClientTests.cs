using System.Net;
using EmbyRadarr.Radarr;

namespace EmbyRadarr.Tests;

public class RadarrClientTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    private readonly FakeHttpClient http = new();
    private readonly RecordingLogger log = new();
    private readonly RadarrClient client;
    private readonly RadarrConnection conn = new("http://radarr.local:7878/radarr/", Key);

    public RadarrClientTests()
    {
        this.client = new RadarrClient(this.http, this.log);
    }

    [Fact]
    public async Task Parses_status_profiles_and_root_folders_and_ignores_unknown_fields()
    {
        this.http.Responses["/radarr/api/v3/system/status"] = (HttpStatusCode.OK,
            """{"appName":"Radarr","instanceName":"Radarr","version":"5.14.0.9383","isDocker":true,"startTime":"2026-10-20T10:00:00Z"}""");
        this.http.Responses["/radarr/api/v3/qualityprofile"] = (HttpStatusCode.OK,
            """[{"id":1,"name":"Any","upgradeAllowed":false,"cutoff":20,"items":[{"quality":{"id":1}}]},{"id":4,"name":"HD-1080p"}]""");
        this.http.Responses["/radarr/api/v3/rootfolder"] = (HttpStatusCode.OK,
            """[{"id":1,"path":"/movies","accessible":true,"freeSpace":123,"unmappedFolders":[]}]""");

        var status = await this.client.GetSystemStatus(this.conn, default);
        var profiles = await this.client.GetQualityProfiles(this.conn, default);
        var folders = await this.client.GetRootFolders(this.conn, default);

        Assert.Equal("Radarr", status.AppName);
        Assert.Equal("5.14.0.9383", status.Version);
        Assert.Equal(new[] { "Any", "HD-1080p" }, profiles.Select(p => p.Name));
        Assert.Equal(4, profiles[1].Id);
        Assert.Equal("/movies", folders.Single().Path);
        Assert.True(folders.Single().Accessible);
    }

    [Fact]
    public async Task Sends_key_in_header_not_in_url_and_trims_trailing_slash()
    {
        this.http.Responses["/radarr/api/v3/system/status"] = (HttpStatusCode.OK, "{}");

        await this.client.GetSystemStatus(this.conn, default);

        var req = this.http.Requests.Single();
        Assert.Equal("http://radarr.local:7878/radarr/api/v3/system/status", req.Url);
        Assert.Equal(Key, req.RequestHeaders["X-Api-Key"]);
        Assert.DoesNotContain(Key, req.Url);
        Assert.False(req.LogRequest);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API key")]
    [InlineData(HttpStatusCode.NotFound, "URL base")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    public async Task Maps_http_errors_to_friendly_messages(HttpStatusCode status, string expected)
    {
        this.http.Responses["/radarr/api/v3/system/status"] = (status, "");

        var ex = await Assert.ThrowsAsync<RadarrException>(() => this.client.GetSystemStatus(this.conn, default));

        Assert.Contains(expected, ex.Message);
        this.AssertKeyNeverLogged();
    }

    [Fact]
    public async Task Connection_failure_becomes_friendly_message()
    {
        this.http.Throw = new HttpRequestException("Connection refused");

        var ex = await Assert.ThrowsAsync<RadarrException>(() => this.client.GetSystemStatus(this.conn, default));

        Assert.Contains("Could not reach Radarr", ex.Message);
        this.AssertKeyNeverLogged();
    }

    [Fact]
    public async Task Html_instead_of_json_becomes_friendly_message()
    {
        this.http.Responses["/radarr/api/v3/system/status"] = (HttpStatusCode.OK, "<html>login</html>");

        var ex = await Assert.ThrowsAsync<RadarrException>(() => this.client.GetSystemStatus(this.conn, default));

        Assert.Contains("could not read", ex.Message);
    }

    [Theory]
    [InlineData("", Key, "not configured")]
    [InlineData("http://radarr:7878", "", "not configured")]
    [InlineData("radarr:7878", Key, "http://")]
    [InlineData("ftp://radarr", Key, "http://")]
    public async Task Rejects_bad_settings_without_calling_radarr(string url, string key, string expected)
    {
        var ex = await Assert.ThrowsAsync<RadarrException>(() => this.client.GetSystemStatus(new RadarrConnection(url, key), default));

        Assert.Contains(expected, ex.Message);
        Assert.Empty(this.http.Requests);
    }

    private void AssertKeyNeverLogged()
        => Assert.DoesNotContain(this.log.Lines, l => l.Contains(Key));
}
