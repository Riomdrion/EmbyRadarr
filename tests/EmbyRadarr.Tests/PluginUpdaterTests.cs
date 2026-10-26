using System.Net;
using System.Security.Cryptography;
using EmbyRadarr.Radarr;
using EmbyRadarr.Update;

namespace EmbyRadarr.Tests;

public class PluginUpdaterTests : IDisposable
{
    private static readonly byte[] NewDll = "MZ new plugin bytes"u8.ToArray();

    private readonly FakeGitHub github = new();
    private readonly PluginUpdater updater;
    private readonly string dir = Directory.CreateTempSubdirectory("embyradarr-").FullName;
    private readonly string target;

    public PluginUpdaterTests()
    {
        this.updater = new PluginUpdater(new RecordingLogger(), this.github);
        this.target = Path.Combine(this.dir, "EmbyRadarr.dll");
        File.WriteAllText(this.target, "MZ old plugin");
    }

    public void Dispose() => Directory.Delete(this.dir, true);

    private void PublishRelease(string tag, byte[] bytes, string? digest = null)
    {
        digest ??= "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        this.github.Responses["https://api.github.com/repos/Riomdrion/EmbyRadarr/releases/latest"] = (HttpStatusCode.OK, System.Text.Encoding.UTF8.GetBytes($$"""
            {"tag_name":"{{tag}}","name":"{{tag}}","assets":[
              {"name":"EmbyRadarr.dll","size":{{bytes.Length}},"digest":"{{digest}}",
               "url":"https://api.github.com/repos/Riomdrion/EmbyRadarr/releases/assets/1",
               "browser_download_url":"https://github.com/Riomdrion/EmbyRadarr/releases/download/{{tag}}/EmbyRadarr.dll"}]}
            """));
        this.github.Responses["https://api.github.com/repos/Riomdrion/EmbyRadarr/releases/assets/1"] = (HttpStatusCode.OK, bytes);
        this.github.Responses[$"https://github.com/Riomdrion/EmbyRadarr/releases/download/{tag}/EmbyRadarr.dll"] = (HttpStatusCode.OK, bytes);
    }

    [Theory]
    [InlineData("v0.3.0", "0.2.0.0", true)]
    [InlineData("v0.2.0", "0.2.0.0", false)]
    [InlineData("v0.1.9", "0.2.0.0", false)]
    [InlineData("0.10.0", "0.9.0.0", true)]
    public async Task Detects_newer_release(string tag, string installed, bool newer)
    {
        this.PublishRelease(tag, NewDll);

        var info = await this.updater.CheckLatest(Version.Parse(installed), null, default);

        Assert.Equal(newer, info.IsNewer);
        Assert.Equal(tag, info.Tag);
    }

    [Fact]
    public async Task Private_repo_without_token_explains_what_to_do()
    {
        var ex = await Assert.ThrowsAsync<RadarrException>(() => this.updater.CheckLatest(new Version(0, 2, 0), null, default));

        Assert.Contains("private", ex.Message);
    }

    [Fact]
    public async Task Token_is_sent_only_when_given_and_selects_the_api_download_url()
    {
        this.PublishRelease("v0.3.0", NewDll);

        var info = await this.updater.CheckLatest(new Version(0, 2, 0), "ghp_secret", default);
        await this.updater.Install(info, "ghp_secret", this.target, default);

        Assert.All(this.github.Requests, r => Assert.Equal("Bearer ghp_secret", r.Headers.Authorization?.ToString()));
        Assert.Equal("https://api.github.com/repos/Riomdrion/EmbyRadarr/releases/assets/1", this.github.Requests.Last().RequestUri!.ToString());
        Assert.Equal("application/octet-stream", this.github.Requests.Last().Headers.Accept.Single().MediaType);
    }

    [Fact]
    public async Task Install_replaces_the_dll_and_keeps_a_backup()
    {
        this.PublishRelease("v0.3.0", NewDll);

        var info = await this.updater.CheckLatest(new Version(0, 2, 0), null, default);
        await this.updater.Install(info, null, this.target, default);

        Assert.Equal(NewDll, File.ReadAllBytes(this.target));
        Assert.Equal("MZ old plugin", File.ReadAllText(this.target + ".bak"));
        Assert.Null(this.github.Requests.Last().Headers.Authorization);
        Assert.False(File.Exists(this.target + ".download"));
    }

    [Theory]
    [InlineData("not a dll", null, "not a DLL")]
    [InlineData("MZ tampered", "sha256:0000000000000000000000000000000000000000000000000000000000000000", "checksum")]
    public async Task Bad_downloads_leave_the_plugin_untouched(string content, string? digest, string expected)
    {
        this.PublishRelease("v0.3.0", System.Text.Encoding.UTF8.GetBytes(content), digest);

        var info = await this.updater.CheckLatest(new Version(0, 2, 0), null, default);
        var ex = await Assert.ThrowsAsync<RadarrException>(() => this.updater.Install(info, null, this.target, default));

        Assert.Contains(expected, ex.Message);
        Assert.Equal("MZ old plugin", File.ReadAllText(this.target));
    }

    [Fact]
    public async Task Release_without_the_dll_asset_is_reported()
    {
        this.github.Responses["https://api.github.com/repos/Riomdrion/EmbyRadarr/releases/latest"] = (HttpStatusCode.OK, """{"tag_name":"v0.3.0","assets":[]}"""u8.ToArray());

        var ex = await Assert.ThrowsAsync<RadarrException>(() => this.updater.CheckLatest(new Version(0, 2, 0), null, default));

        Assert.Contains("has no EmbyRadarr.dll", ex.Message);
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, byte[] Body)> Responses { get; } = new();

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests.Add(request);
            var (status, body) = this.Responses.TryGetValue(request.RequestUri!.ToString(), out var r) ? r : (HttpStatusCode.NotFound, Array.Empty<byte>());
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }
}
