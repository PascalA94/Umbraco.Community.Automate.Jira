using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Community.Automate.Jira.Configuration;

namespace Umbraco.Community.Automate.Jira.Webhooks;

/// <summary>
/// Rejects Jira deliveries whose signed <c>timestamp</c> is outside the window, and any body already accepted
/// for the same automation (F-03, design 6.5 steps 5 and 6). Runs only after the signature has passed, so
/// unauthenticated traffic cannot fill the cache.
/// </summary>
/// <remarks>
/// Registered as a singleton: the lock below only makes check-and-add atomic if every request shares one guard.
/// The cache is per node; on a load-balanced site a replay to another node inside the window passes (README).
/// It is the guard's own bounded MemoryCache, not the site's shared IMemoryCache (SEC16-2): a shared cache can be
/// compacted or hit a SizeLimit set elsewhere and silently drop an entry, which would let a replay through.
/// </remarks>
internal sealed class WebhookReplayGuard : IDisposable
{
    /// <summary>The largest epoch-millisecond value <see cref="DateTimeOffset"/> can hold (9999-12-31).</summary>
    public const long MaxTimestamp = 253402300799999;

    /// <summary>
    /// Entries kept at once. At the default 120-minute window that is about 14 accepted deliveries a second,
    /// sustained; beyond it the guard refuses new deliveries (fail closed) and logs a warning.
    /// </summary>
    public const int DefaultMaxEntries = 100_000;

    private readonly object _gate = new();
    private readonly MemoryCache _cache;
    private readonly TimeProvider _time;
    private readonly IOptions<JiraOptions> _options;
    private readonly ILogger<WebhookReplayGuard> _logger;

    /// <summary>Creates the guard. <paramref name="maxEntries"/> is for tests; the site uses the default.</summary>
    public WebhookReplayGuard(TimeProvider time, IOptions<JiraOptions> options, ILogger<WebhookReplayGuard> logger, int maxEntries = DefaultMaxEntries)
    {
        _time = time;
        _options = options;
        _logger = logger;
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxEntries });
    }

    /// <inheritdoc />
    public void Dispose() => _cache.Dispose();

    /// <summary>
    /// Accepts the body once per <paramref name="replayScope"/> while its timestamp is in the window.
    /// Never throws for a malformed body; <paramref name="reason"/> says why it was refused, without body content.
    /// </summary>
    public bool TryAccept(string replayScope, ReadOnlySpan<byte> body, out string? reason)
    {
        if (!TryReadTimestamp(body, out var timestamp))
        {
            reason = "The body has no integer epoch-millisecond timestamp.";
            return false;
        }

        var window = TimeSpan.FromMinutes(_options.Value.ReplayWindowMinutes);
        var now = _time.GetUtcNow();
        var sent = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);

        // Exclusive at both ends, so the entry's lifetime (sent + window - now) is always positive (SEC16-3).
        if (sent <= now - window || sent >= now + window)
        {
            reason = "The timestamp is outside the replay window.";
            return false;
        }

        // The entry must outlive the moment the timestamp leaves the window. A body stamped up to one window in
        // the future stays acceptable for nearly two windows, so "one window from first sight" would let it
        // through again (Replay_FutureTimestamp_BlockedUntilTimestampLeavesWindow).
        var expires = sent + window;
        var key = $"jira:wh:{replayScope}:{Convert.ToHexStringLower(SHA256.HashData(body))}";

        // Plan P5: two identical concurrent deliveries must not both pass, so the check and the add are one step.
        lock (_gate)
        {
            // The expiry is kept in the entry and checked against our TimeProvider, because the cache's own clock
            // is the system clock and the window must follow the same clock as the timestamp check.
            if (_cache.TryGetValue(key, out DateTimeOffset seenUntil) && seenUntil > now)
            {
                reason = "This delivery was already accepted (replay).";
                return false;
            }

            _cache.Set(key, expires, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expires - now,
                Priority = CacheItemPriority.NeverRemove,
                Size = 1,
            });

            // A full cache drops the new entry without telling us; a delivery we cannot remember is refused.
            if (!_cache.TryGetValue(key, out _))
            {
                _logger.LogWarning("Jira webhook replay cache is full ({MaxEntries} entries); refusing deliveries until entries expire.", _cache.Count);
                reason = "The replay cache is at capacity.";
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static bool TryReadTimestamp(ReadOnlySpan<byte> body, out long timestamp)
    {
        timestamp = 0;

        // Utf8JsonReader does not skip a byte order mark the way JsonDocument.Parse(stream) does.
        if (body.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            body = body[3..];
        }

        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = 32 });
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var found = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.NameEquals("timestamp"))
                {
                    continue;
                }

                // A repeated key is ambiguous between parsers, so it is refused rather than first- or last-wins.
                if (found)
                {
                    return false;
                }

                found = true;
                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt64(out timestamp))
                {
                    return false;
                }
            }

            return found && timestamp is >= 0 and <= MaxTimestamp;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
