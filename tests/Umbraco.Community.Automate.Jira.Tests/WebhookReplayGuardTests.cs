using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Community.Automate.Jira.Configuration;
using Umbraco.Community.Automate.Jira.Tests.Support;
using Umbraco.Community.Automate.Jira.Webhooks;

namespace Umbraco.Community.Automate.Jira.Tests;

/// <summary>The replay guard on its own (F-03, plan P5), with a fake clock and the default 120-minute window.</summary>
public class WebhookReplayGuardTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(120);

    private static (WebhookReplayGuard Guard, FakeTimeProvider Time) Create(int maxEntries = WebhookReplayGuard.DefaultMaxEntries)
    {
        var time = new FakeTimeProvider(WebhookRig.Start);
        var guard = new WebhookReplayGuard(time, Options.Create(new JiraOptions()), NullLogger<WebhookReplayGuard>.Instance, maxEntries);
        return (guard, time);
    }

    private static byte[] Body(long timestamp, string extra = "") =>
        Encoding.UTF8.GetBytes($"{{\"timestamp\":{timestamp},\"webhookEvent\":\"jira:issue_updated\"{extra}}}");

    private static byte[] Body(DateTimeOffset at, string extra = "") => Body(at.ToUnixTimeMilliseconds(), extra);

    // QA16-1: proves the "no exception" claim at the guard, where no outer catch can hide a throw.
    [Theory]
    [InlineData("\"1762248600000\"")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("1e30")]
    [InlineData("1762248600000.0")]
    [InlineData("253402300800000")]
    [InlineData("99999999999999999999999")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public void TryAccept_MalformedTimestamp_RejectsWithReasonAndNeverThrows(string raw)
    {
        var (guard, _) = Create();
        var body = Encoding.UTF8.GetBytes($"{{\"timestamp\":{raw},\"webhookEvent\":\"x\"}}");

        string? reason = null;
        var accepted = true;
        var ex = Record.Exception(() => accepted = guard.TryAccept("scope", body, out reason));

        Assert.Null(ex);
        Assert.False(accepted);
        Assert.Contains("timestamp", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1]")]
    public void TryAccept_NotAnObject_RejectsWithoutThrowing(string text)
    {
        var (guard, _) = Create();

        Assert.False(guard.TryAccept("scope", Encoding.UTF8.GetBytes(text), out var reason));
        Assert.NotNull(reason);
    }

    // QA16-2, QA16-6, SEC16-3: the window is exclusive at both ends, and the exact edge neither throws nor passes.
    [Fact]
    public void TryAccept_ExactWindowEdges_Rejected()
    {
        var (guard, time) = Create();
        var now = time.GetUtcNow();

        Assert.False(guard.TryAccept("scope", Body(now - Window), out var past));
        Assert.Contains("window", past, StringComparison.OrdinalIgnoreCase);
        Assert.False(guard.TryAccept("scope", Body(now + Window), out var future));
        Assert.Contains("window", future, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryAccept_OneMillisecondInsideTheEdges_Accepted()
    {
        var (guard, time) = Create();
        var now = time.GetUtcNow();
        var ms = TimeSpan.FromMilliseconds(1);

        Assert.True(guard.TryAccept("scope", Body(now - Window + ms), out _));
        Assert.True(guard.TryAccept("scope", Body(now + Window - ms), out _));
    }

    // An accepted body stays blocked right up to the moment its timestamp leaves the window.
    [Fact]
    public void TryAccept_ReplayOneMillisecondBeforeExpiry_StillRejectedAsReplay()
    {
        var (guard, time) = Create();
        var body = Body(time.GetUtcNow());

        Assert.True(guard.TryAccept("scope", body, out _));
        time.Advance(Window - TimeSpan.FromMilliseconds(1));

        Assert.False(guard.TryAccept("scope", body, out var reason));
        Assert.Contains("replay", reason, StringComparison.OrdinalIgnoreCase);
    }

    // SEC16-2: the guard owns its cache, so the site's shared cache being compacted cannot drop replay entries.
    [Fact]
    public void SharedCacheCompaction_DoesNotDropReplayEntries()
    {
        var rig = new WebhookRig();
        var guard = rig.Provider.GetRequiredService<WebhookReplayGuard>();
        var body = Encoding.UTF8.GetBytes(rig.Body());

        Assert.True(guard.TryAccept("scope", body, out _));
        ((MemoryCache)rig.Provider.GetRequiredService<IMemoryCache>()).Compact(1.0);

        Assert.False(guard.TryAccept("scope", body, out _));
    }

    // SEC16-2: when the guard's cache is full it fails closed, rather than accepting a body it cannot remember.
    [Fact]
    public void TryAccept_CacheFull_FailsClosed()
    {
        var (guard, time) = Create(maxEntries: 2);
        var now = time.GetUtcNow();

        Assert.True(guard.TryAccept("scope", Body(now, ",\"n\":1"), out _));
        Assert.True(guard.TryAccept("scope", Body(now, ",\"n\":2"), out _));

        Assert.False(guard.TryAccept("scope", Body(now, ",\"n\":3"), out var reason));
        Assert.Contains("capacity", reason, StringComparison.OrdinalIgnoreCase);

        // And the bodies it did accept are still blocked.
        Assert.False(guard.TryAccept("scope", Body(now, ",\"n\":1"), out reason));
        Assert.Contains("replay", reason, StringComparison.OrdinalIgnoreCase);
    }

    // QA16-5: dedicated threads released together by a barrier, over many bodies, so a missing lock shows up.
    [Fact]
    public void TryAccept_ConcurrentSameBody_ExactlyOneAcceptedPerBody()
    {
        const int bodies = 100;
        const int threads = 16;
        var (guard, time) = Create();
        var now = time.GetUtcNow();

        for (var b = 0; b < bodies; b++)
        {
            var body = Body(now, $",\"n\":{b}");
            var accepted = 0;
            using var barrier = new Barrier(threads);
            var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
            {
                barrier.SignalAndWait();
                if (guard.TryAccept("scope", body, out _))
                {
                    Interlocked.Increment(ref accepted);
                }
            })).ToList();
            workers.ForEach(w => w.Start());
            workers.ForEach(w => w.Join());

            Assert.Equal(1, accepted);
        }
    }
}
