using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Umbraco.Community.Automate.Jira.Tests.Support;

/// <summary>One request as the fake saw it, copied at send time because the message is disposed afterwards.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Authorization,
    IReadOnlyDictionary<string, string[]> Headers,
    string? Body);

/// <summary>
/// A queue of canned responses standing in for Jira. Records every request, including headers, so tests can
/// assert on what was (and was not) sent.
/// </summary>
internal sealed class FakeJiraHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly object _gate = new();

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    public FakeJiraHandler EnqueueJson(HttpStatusCode status, string? json = null, params (string Name, string Value)[] headers)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (json is not null)
            {
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            foreach (var (name, value) in headers)
            {
                if (!response.Headers.TryAddWithoutValidation(name, value))
                {
                    response.Content ??= new StringContent(string.Empty);
                    response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            return response;
        });
        return this;
    }

    /// <summary>A response whose body fails mid-read, as a dropped connection does (HttpIOException is an IOException).</summary>
    public FakeJiraHandler EnqueueBrokenBody(HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(status) { Content = new StreamContent(new BrokenStream()) });
        return this;
    }

    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection reset SENTINEL-IO");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("connection reset SENTINEL-IO");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public FakeJiraHandler EnqueueException(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), headers, body));
        }

        if (!_responses.TryDequeue(out var next))
        {
            throw new InvalidOperationException("FakeJiraHandler has no canned response left for this request.");
        }

        return next(request);
    }
}
