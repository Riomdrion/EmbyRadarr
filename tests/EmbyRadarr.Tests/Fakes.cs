using System.Net;
using System.Text;
using MediaBrowser.Common.Net;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;
using MediaBrowser.Model.Logging;

namespace EmbyRadarr.Tests;

/// <summary>Stands in for Emby's IHttpClient. Returns canned responses per URL path.</summary>
internal sealed class FakeHttpClient : IHttpClient
{
    public Dictionary<string, (HttpStatusCode Status, string Body)> Responses { get; } = new();

    public Exception? Throw { get; set; }

    public List<HttpRequestOptions> Requests { get; } = new();

    public Task<HttpResponseInfo> SendAsync(HttpRequestOptions options, string httpMethod)
    {
        this.Requests.Add(options);
        if (this.Throw != null)
        {
            throw this.Throw;
        }

        var path = new Uri(options.Url).AbsolutePath;
        var (status, body) = this.Responses.TryGetValue(path, out var r) ? r : (HttpStatusCode.NotFound, "");
        return Task.FromResult(new HttpResponseInfo
        {
            StatusCode = status,
            Content = new MemoryStream(Encoding.UTF8.GetBytes(body)),
        });
    }

    public Task<HttpResponseInfo> GetResponse(HttpRequestOptions options) => this.SendAsync(options, "GET");
    public Task<Stream> Get(HttpRequestOptions options) => throw new NotSupportedException();
    public Task<HttpResponseInfo> Post(HttpRequestOptions options) => this.SendAsync(options, "POST");
    public Task<string> GetTempFile(HttpRequestOptions options) => throw new NotSupportedException();
    public Task<HttpResponseInfo> GetTempFileResponse(HttpRequestOptions options) => throw new NotSupportedException();
    public IDisposable GetConnectionContext(HttpRequestOptions options) => throw new NotSupportedException();
}

/// <summary>Records every formatted log line so tests can check the API key never leaks.</summary>
internal sealed class RecordingLogger : ILogger
{
    public List<string> Lines { get; } = new();

    private void Add(string message, object[] args, Exception? ex = null)
        => this.Lines.Add((args.Length > 0 ? string.Format(message, args) : message) + (ex != null ? " " + ex : ""));

    public void Info(string message, params object[] paramList) => this.Add(message, paramList);
    public void Error(string message, params object[] paramList) => this.Add(message, paramList);
    public void Warn(string message, params object[] paramList) => this.Add(message, paramList);
    public void Debug(string message, params object[] paramList) => this.Add(message, paramList);
    public void Fatal(string message, params object[] paramList) => this.Add(message, paramList);
    public void FatalException(string message, Exception exception, params object[] paramList) => this.Add(message, paramList, exception);
    public void ErrorException(string message, Exception exception, params object[] paramList) => this.Add(message, paramList, exception);
    public void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent) => this.Lines.Add(message + additionalContent);
    public void Log(LogSeverity severity, string message, params object[] paramList) => this.Add(message, paramList);
    public void Log(LogSeverity severity, ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
    public void Error(ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
    public void Warn(ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
    public void Info(ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
    public void Debug(ReadOnlyMemory<char> message) => this.Lines.Add(message.ToString());
}
