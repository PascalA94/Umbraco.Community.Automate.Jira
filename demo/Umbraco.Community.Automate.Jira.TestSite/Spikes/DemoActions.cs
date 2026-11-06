using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Settings;

namespace Umbraco.Community.Automate.Jira.TestSite.Spikes;

// Demo-only actions for the Task 3 spikes. They live in the demo site, never in src/, so the
// package ships no spike aliases.

public sealed class DumpContextSettings
{
    [Field(Label = "Label")]
    public string Label { get; set; } = string.Empty;
}

public sealed class DumpContextOutput
{
    public string Dump { get; init; } = string.Empty;
}

/// <summary>
/// S2 step 3a: records every public property of <see cref="ActionContext"/> (name and value) so that
/// a ForEach iteration identity, if the context carries one, is visible in the stored run.
/// </summary>
[Action("demo.dumpContext", "Demo: dump action context")]
public sealed class DumpContextAction(ActionInfrastructure infrastructure) : ActionBase<DumpContextSettings, DumpContextOutput>(infrastructure)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, MaxDepth = 16 };

    public override Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        foreach (var property in typeof(ActionContext).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            lines.Add($"{property.Name} = {Describe(property.Name, property.GetValue(context))}");
        }

        foreach (var line in lines)
        {
            context.LogInfo(line);
        }

        var dump = string.Join("\n", lines);
        return Task.FromResult(Success(new DumpContextOutput { Dump = dump }));
    }

    private static string Describe(string name, object? value)
    {
        if (value is null)
        {
            return "null";
        }

        // Long or recursive graphs (the action instance, the execution context) are reduced to their type.
        if (name is "Action" or "ExecutionContext" or "Connection" or "CancellationToken" or "LogEntries")
        {
            return $"<{value.GetType().Name}>";
        }

        try
        {
            return JsonSerializer.Serialize(value, Json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return $"<{value.GetType().Name}> (not serialisable)";
        }
    }
}

public sealed class FailSettings
{
    [Field(Label = "Label")]
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// S6 / step 3b: always fails with an unclassified (transient) error and records when each attempt ran,
/// to measure Automate's retry delays.
/// </summary>
[Action("demo.failTransient", "Demo: fail transiently")]
public sealed class FailTransientAction(ActionInfrastructure infrastructure, ILogger<FailTransientAction> logger) : ActionBase<FailSettings>(infrastructure)
{
    public static ConcurrentQueue<DateTimeOffset> Attempts { get; } = new();

    public override Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        Attempts.Enqueue(now);
        logger.LogWarning("demo.failTransient attempt at {At:O}", now);
        return Task.FromResult(ActionResult.Failed(new IOException("demo transient failure")));
    }
}
