using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Umbraco.Automate.Core.Execution;
using System.Text;
using System.Text.Json;
using Moq;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Actions;

namespace Umbraco.Community.Automate.Jira.Tests;

public class DedupeKeyTests
{
    private static readonly Guid Automation = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Run = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Step = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static ActionContext Context(Guid? automation = null, Guid? run = null, Guid? step = null, object? loop = null) => new()
    {
        AutomationId = automation ?? Automation,
        RunId = run ?? Run,
        StepId = step ?? Step,
        ActionAlias = "community.jira.createIssue",
        Settings = null,
        Action = Mock.Of<IAction>(),
        BindingData = loop is null ? new Dictionary<string, object?>() : new Dictionary<string, object?> { ["loop"] = loop },
    };

    private static readonly string?[] Inputs = ["SBX", "Task", "Summary", "Body"];

    [Fact]
    public void Effective_SameContextAndInputs_IsStable()
    {
        Assert.Equal(DedupeKey.Effective(null, Context(), Inputs), DedupeKey.Effective(null, Context(), [.. Inputs]));
    }

    [Fact]
    public void Effective_DefaultShape()
    {
        var key = DedupeKey.Effective(null, Context(), Inputs);

        Assert.Matches($"^{Automation:D}:{Run:D}:{Step:D}:-:[0-9a-f]{{16}}$", key);
    }

    [Fact]
    public void Effective_DifferentStep_Differs() =>
        Assert.NotEqual(DedupeKey.Effective(null, Context(), Inputs), DedupeKey.Effective(null, Context(step: Guid.NewGuid()), Inputs));

    [Fact]
    public void Effective_DifferentRun_Differs() =>
        Assert.NotEqual(DedupeKey.Effective(null, Context(), Inputs), DedupeKey.Effective(null, Context(run: Guid.NewGuid()), Inputs));

    [Fact]
    public void Effective_DifferentAutomation_Differs() =>
        Assert.NotEqual(DedupeKey.Effective(null, Context(), Inputs), DedupeKey.Effective(null, Context(automation: Guid.NewGuid()), Inputs));

    [Fact]
    public void Effective_ForEachItemsWithDifferentInputs_Differ() =>
        Assert.NotEqual(DedupeKey.Effective(null, Context(), ["SBX", "Task", "Item A", null]), DedupeKey.Effective(null, Context(), ["SBX", "Task", "Item B", null]));

    // Spike S3a: the iteration identity is BindingData["loop"].index, in whatever shape Automate stores it.
    public static TheoryData<object, object> LoopShapes() => new()
    {
        { new Dictionary<string, object?> { ["item"] = "a", ["index"] = 0 }, new Dictionary<string, object?> { ["item"] = "a", ["index"] = 1 } },
        { JsonSerializer.SerializeToElement(new { item = "a", index = 0 }), JsonSerializer.SerializeToElement(new { item = "a", index = 1 }) },
        { new { item = "a", index = 0 }, new { item = "a", index = 1 } },
    };

    [Theory]
    [MemberData(nameof(LoopShapes))]
    public void Effective_ForEachIteration_Differs(object first, object second)
    {
        var a = DedupeKey.Effective(null, Context(loop: first), Inputs);
        var b = DedupeKey.Effective(null, Context(loop: second), Inputs);

        Assert.NotEqual(a, b);
        Assert.Contains(":0:", a, StringComparison.Ordinal);
        Assert.Contains(":1:", b, StringComparison.Ordinal);
    }

    [Fact]
    public void Effective_JsonObjectLoop_ReadsIndex()
    {
        var a = DedupeKey.Effective(null, Context(loop: new JsonObject { ["item"] = "a", ["index"] = 0 }), Inputs);
        var b = DedupeKey.Effective(null, Context(loop: new JsonObject { ["item"] = "a", ["index"] = 1 }), Inputs);

        Assert.NotEqual(a, b);
    }

    // The loop value as Automate's own BindingDataBuilder builds it, on whichever line this runs.
    private static IReadOnlyDictionary<string, object?> RealBindingData(int index)
    {
        var core = typeof(ActionContext).Assembly;
        var data = new AutomationWorkflowData { RunId = Run, AutomationId = Automation, TriggerOutput = new Dictionary<string, object?>() };
        var iterationType = core.GetType("Umbraco.Automate.Core.Execution.ControlFlow.ForEachIterationContext", throwOnError: true)!;
        var iteration = Activator.CreateInstance(iterationType, "same item", index, Guid.NewGuid(), null);
        var build = core.GetType("Umbraco.Automate.Core.Execution.BindingDataBuilder", throwOnError: true)!
            .GetMethod("Build", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        var arguments = build.GetParameters().Select(p => p.ParameterType == typeof(AutomationWorkflowData) ? data
            : p.ParameterType == iterationType ? iteration
            : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
            : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        return (IReadOnlyDictionary<string, object?>)build.Invoke(null, arguments)!;
    }

    [Fact]
    public void Effective_RealAutomateLoopShape_IndexRead()
    {
        var first = RealBindingData(0);
        var second = RealBindingData(1);
        Assert.True(first.ContainsKey("loop"));

        var a = DedupeKey.Effective(null, Context(loop: first["loop"]), Inputs);
        var b = DedupeKey.Effective(null, Context(loop: second["loop"]), Inputs);

        Assert.Contains(":0:", a, StringComparison.Ordinal);
        Assert.Contains(":1:", b, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("odd")]
    [InlineData(42)]
    public void Effective_LoopWithUnreadableIndex_FailsClosed(object loop)
    {
        // Inside a loop with no readable index, identical items would silently share one issue (F-05).
        Assert.Throws<DedupeKeyUnavailableException>(() => DedupeKey.Effective(null, Context(loop: loop), Inputs));
        Assert.Equal("form-1", DedupeKey.Effective("form-1", Context(loop: loop), Inputs));
    }

    [Fact]
    public void Effective_InputsAreLengthPrefixed() =>
        Assert.NotEqual(DedupeKey.Effective(null, Context(), ["ab", "c"]), DedupeKey.Effective(null, Context(), ["a", "bc"]));

    [Fact]
    public void Effective_NullAndEmptyInputs_Differ() =>
        Assert.NotEqual(DedupeKey.Effective(null, Context(), [null]), DedupeKey.Effective(null, Context(), [string.Empty]));

    [Fact]
    public void Effective_UserKey_IgnoresContextAndInputs()
    {
        Assert.Equal("form-123", DedupeKey.Effective("form-123", Context(), Inputs));
        Assert.Equal("form-123", DedupeKey.Effective("form-123", Context(run: Guid.NewGuid(), step: Guid.NewGuid()), ["other"]));
    }

    [Fact]
    public void Effective_UserKey_Trimmed() => Assert.Equal("form-123", DedupeKey.Effective("  form-123 \n", Context(), Inputs));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Effective_BlankUserKey_UsesDefault(string? userKey) =>
        Assert.Equal(DedupeKey.Effective(null, Context(), Inputs), DedupeKey.Effective(userKey, Context(), Inputs));

    [Fact]
    public void Marker_Is16LowerHex() => Assert.Matches("^[0-9a-f]{16}$", DedupeKey.Marker("form-123", Guid.NewGuid()));

    [Fact]
    public void Marker_DependsOnConnectionId() =>
        Assert.NotEqual(DedupeKey.Marker("form-123", Guid.NewGuid()), DedupeKey.Marker("form-123", Guid.NewGuid()));

    [Fact]
    public void Marker_IsStableForSameInputs()
    {
        var connection = Guid.NewGuid();
        Assert.Equal(DedupeKey.Marker("form-123", connection), DedupeKey.Marker("form-123", connection));
    }

    [Fact]
    public void Marker_IsHmacKeyedWithConnectionIdBytes()
    {
        var connection = Guid.NewGuid();
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(connection.ToByteArray(), Encoding.UTF8.GetBytes("form-123")))[..16];

        Assert.Equal(expected, DedupeKey.Marker("form-123", connection));
    }

    [Fact]
    public void Marker_IsNotPlainSha256()
    {
        var plain = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("form-123")))[..16];

        Assert.NotEqual(plain, DedupeKey.Marker("form-123", Guid.NewGuid()));
    }

    [Fact]
    public void MarkerKind_IsLabel() => Assert.Equal(DedupeMarker.Label, DedupeKey.MarkerKind);
}
