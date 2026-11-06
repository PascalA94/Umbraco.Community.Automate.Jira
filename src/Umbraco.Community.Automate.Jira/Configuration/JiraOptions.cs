namespace Umbraco.Community.Automate.Jira.Configuration;

/// <summary>
/// Optional settings bound from <c>Umbraco:Automate:Jira</c>. Everything else is on the connection.
/// </summary>
/// <remarks>
/// Deliberately has no member for the <c>Credentials</c> section: a credential bound into an options object
/// could be dumped by diagnostics. Only the credential store reads that section.
/// </remarks>
public sealed class JiraOptions
{
    private int _timeoutSeconds = 30;
    private int _replayWindowMinutes = 120;

    /// <summary>The <see cref="HttpClient"/> timeout in seconds. Values below 1 are raised to 1.</summary>
    public int TimeoutSeconds
    {
        // HttpClient.Timeout throws on zero or negative, which would fail every request at client creation.
        get => _timeoutSeconds;
        set => _timeoutSeconds = Math.Max(1, value);
    }

    /// <summary>How long the issue-type lookup is cached, in minutes.</summary>
    public int IssueTypeCacheMinutes { get; set; } = 10;

    /// <summary>
    /// The webhook timestamp window and replay-cache lifetime, in minutes (F-03). Clamped to 10..1440: a window
    /// under 10 minutes rejects deliveries on ordinary clock skew, and one over a day grows the replay cache.
    /// </summary>
    public int ReplayWindowMinutes
    {
        get => _replayWindowMinutes;
        set => _replayWindowMinutes = Math.Clamp(value, 10, 1440);
    }
}
