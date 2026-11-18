#if AUTOMATE_19
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.HostedServices;
using Umbraco.Cms.Core.Models.ServerEvents;
using Umbraco.Cms.Core.ServerEvents;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Serialization;
using Umbraco.Cms.Infrastructure.Sync;

namespace Umbraco.Community.Automate.Jira.IntegrationTests;

// Copies of Umbraco's own test doubles (tests/Umbraco.Tests.Integration/Testing/Search at release-19.0.0-rc1). They
// are internal in Umbraco.Cms.Tests.Integration (CS0122), so they cannot be referenced. Together they make Umbraco
// Search index a saved node from the test thread instead of a background queue the harness does not run.

/// <summary>Runs queued background work at once, on the caller's thread.</summary>
internal sealed class ImmediateBackgroundTaskQueue : IBackgroundTaskQueue
{
    public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
        => workItem(CancellationToken.None).GetAwaiter().GetResult();

    public Task<Func<CancellationToken, Task>?> DequeueAsync(CancellationToken cancellationToken)
        => throw new NotImplementedException();
}

/// <summary>Delivers cache refresher messages locally only, so index notifications fire in the test host.</summary>
internal sealed class LocalServerMessenger : ServerMessengerBase
{
    public LocalServerMessenger()
        : base(false, new SystemTextJsonSerializer(new DefaultJsonSerializerEncoderFactory()))
    {
    }

    public override void SendMessages()
    {
    }

    public override void Sync()
    {
    }

    protected override void DeliverRemote(ICacheRefresher refresher, MessageType messageType, IEnumerable<object>? ids = null, string? json = null)
    {
    }
}

/// <summary>Drops server events; the tests have no backoffice clients to notify.</summary>
internal sealed class NoOpServerEventRouter : IServerEventRouter
{
    public Task RouteEventAsync(ServerEvent serverEvent) => Task.CompletedTask;

    public Task RouteEventAsync(ServerEvent serverEvent, ServerEventRoutingContext context) => Task.CompletedTask;

    public Task NotifyUserAsync(ServerEvent serverEvent, Guid userKey) => Task.CompletedTask;

    public Task BroadcastEventAsync(ServerEvent serverEvent) => Task.CompletedTask;
}
#endif
