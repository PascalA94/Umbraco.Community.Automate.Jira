using System.Text.Json;

namespace Umbraco.Community.Automate.Jira.Tests;

/// <summary>
/// Review Focus F-01 (Critical), automated (plan review P1): the seeded use case 4 automation is disabled, and every
/// path to Publish Content passes Request Approval, a second Find on the approved branch and an UpdateDate re-check.
/// </summary>
public class UseCase4SeedTests
{
    private const string Trigger = "00000000-0000-0000-0000-000000000000";

    private static string SeedText() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seeds", "usecase4.automation.json"));

    private static JsonElement Seed() => Parse(SeedText());

    private static JsonElement Parse(string text) => JsonDocument.Parse(text).RootElement;

    private sealed record Condition(string Left, string Operator, string Right);

    // The If step's conditions, only when they are one AND-group (several groups are OR-ed, so an extra group could
    // make the check trivially true). Null when the shape is anything else.
    private static IReadOnlyList<Condition>? SingleGroup(JsonElement settings)
    {
        if (!settings.TryGetProperty("conditions", out var conditions)
            || !conditions.TryGetProperty("groups", out var groups)
            || groups.ValueKind != JsonValueKind.Array
            || groups.GetArrayLength() != 1)
        {
            return null;
        }

        return groups[0].GetProperty("conditions").EnumerateArray()
            .Select(c => new Condition(c.GetProperty("leftOperand").GetString()!, c.GetProperty("operator").GetString()!, c.GetProperty("rightOperand").GetString()!))
            .ToList();
    }

    private static bool IsRecheck(Step step, string first, string again) =>
        step.ActionAlias == "umbracoAutomate.if"
        && SingleGroup(step.Settings) is { } c
        && c.Contains(new Condition($"${{ steps.{again}.contentKey }}", "Equals", $"${{ steps.{first}.contentKey }}"))
        && c.Contains(new Condition($"${{ steps.{again}.updateDate }}", "Equals", $"${{ steps.{first}.updateDate }}"))

        // SEC-M3-3: an unresolved binding stringifies to "", and "" Equals "" would pass, so every operand must be set.
        && new[] { $"steps.{again}.contentKey", $"steps.{first}.contentKey", $"steps.{again}.updateDate", $"steps.{first}.updateDate" }
            .All(path => c.Contains(new Condition($"${{ {path} }}", "IsNotEmpty", string.Empty)))
        && c.All(x => x.Operator is "Equals" or "IsNotEmpty");

    private static bool IsStatusChangeCheck(Step step, string read) =>
        step.ActionAlias == "umbracoAutomate.if"
        && SingleGroup(step.Settings) is { } c
        && c.Contains(new Condition($"${{ steps.{read}.webhookEvent }}", "Equals", "jira:issue_updated"))
        && c.Contains(new Condition($"${{ steps.{read}.statusChangedTo }}", "Equals", "Ready to publish"))
        && c.Contains(new Condition($"${{ steps.{read}.statusChangedFrom }}", "NotEquals", $"${{ steps.{read}.statusChangedTo }}"))

        // SEC-M3-1: the issue's own status must be the one the changelog says it moved to.
        && c.Contains(new Condition($"${{ steps.{read}.status }}", "Equals", $"${{ steps.{read}.statusChangedTo }}"));

    private sealed record Edge(string Source, string Target, string? Outcome);

    private sealed record Step(string Id, string ActionAlias, string Alias, JsonElement Settings);

    private static (Dictionary<string, Step> Steps, List<Edge> Edges) Graph(JsonElement seed)
    {
        var automation = seed.GetProperty("automation");
        var steps = automation.GetProperty("steps").EnumerateArray()
            .Select(s => new Step(s.GetProperty("id").GetString()!, s.GetProperty("actionAlias").GetString()!, s.GetProperty("alias").GetString()!, s.GetProperty("settings")))
            .ToDictionary(s => s.Id);
        var edges = automation.GetProperty("connections").EnumerateArray()
            .Select(c => new Edge(c.GetProperty("sourceStepId").GetString()!, c.GetProperty("targetStepId").GetString()!, c.TryGetProperty("outcome", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null))
            .ToList();
        return (steps, edges);
    }

    // Every path from the trigger to a step with the given action alias, as the edges walked.
    private static List<List<Edge>> PathsTo(Dictionary<string, Step> steps, List<Edge> edges, string actionAlias)
    {
        var found = new List<List<Edge>>();
        void Walk(string from, List<Edge> path, HashSet<string> seen)
        {
            foreach (var edge in edges.Where(e => e.Source == from))
            {
                if (!seen.Add(edge.Target))
                {
                    continue;
                }

                var next = new List<Edge>(path) { edge };
                if (steps.TryGetValue(edge.Target, out var step) && step.ActionAlias == actionAlias)
                {
                    found.Add(next);
                }

                Walk(edge.Target, next, seen);
                seen.Remove(edge.Target);
            }
        }

        Walk(Trigger, [], []);
        return found;
    }

    private static string Text(JsonElement settings) => settings.GetRawText();

    [Fact]
    public void UseCase4Seed_IsDisabled()
    {
        Assert.Equal(JsonValueKind.False, Seed().GetProperty("enabled").ValueKind);
    }

    [Fact]
    public void UseCase4Seed_EveryPublishPathPassesApprovalAndRecheck()
    {
        Assert.Null(PublishPathProblem(Seed()));
    }

    // QA-M3-2: a recheck that is present in name only must fail. Each mutation is applied to a compact copy of the
    // real seed, so the cases do not depend on the file's layout.
    [Theory]
    [InlineData("\"operator\":\"Equals\",\"rightOperand\":\"${ steps.find.updateDate }\"", "\"operator\":\"NotEquals\",\"rightOperand\":\"${ steps.find.updateDate }\"")]
    [InlineData("\"rightOperand\":\"${ steps.find.updateDate }\"", "\"rightOperand\":\"${ steps.findAgain.updateDate }\"")]
    [InlineData("\"rightOperand\":\"${ steps.find.contentKey }\"", "\"rightOperand\":\"${ steps.findAgain.contentKey }\"")]
    [InlineData("\"contentKey\":\"${ steps.findAgain.contentKey }\"", "\"contentKey\":\"${ steps.find.contentKey }\"")]
    [InlineData("\"outcome\":\"approved\"", "\"outcome\":\"rejected\"")]
    [InlineData("\"operator\":\"IsNotEmpty\"", "\"operator\":\"IsEmpty\"")]
    public void UseCase4Seed_MutatedRecheckOrPublish_IsCaught(string from, string to)
    {
        var text = Compact();
        Assert.Contains(from, text, StringComparison.Ordinal);

        Assert.NotNull(PublishPathProblem(Parse(text.Replace(from, to, StringComparison.Ordinal))));
    }

    [Fact]
    public void UseCase4Seed_ExtraOrGroupOnRecheck_IsCaught()
    {
        var text = Compact();
        const string marker = "\"leftOperand\":\"${ steps.findAgain.updateDate }\"";
        var groupEnd = text.IndexOf("]}", text.IndexOf(marker, StringComparison.Ordinal), StringComparison.Ordinal);
        var mutated = text.Insert(groupEnd + 2, ",{\"conditions\":[{\"leftOperand\":\"a\",\"operator\":\"Equals\",\"rightOperand\":\"a\"}]}");

        Assert.NotNull(PublishPathProblem(Parse(mutated)));
    }

    private static string Compact() => JsonSerializer.Serialize(Seed(), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // Null when every path to Publish Content passes approval (approved branch), a second Find, and an exact
    // UpdateDate and ContentKey recheck (true branch), and publishes the rechecked key; otherwise what is wrong.
    private static string? PublishPathProblem(JsonElement seed)
    {
        var (steps, edges) = Graph(seed);
        var paths = PathsTo(steps, edges, "umbracoAutomate.publishContent");

        // No path at all would also mean no publish, but then the seed no longer demonstrates the recipe.
        if (paths.Count == 0)
        {
            return "no path to Publish Content";
        }

        foreach (var path in paths)
        {
            var visited = path.Select(e => steps.GetValueOrDefault(e.Target)).ToList();
            var describe = string.Join(" -> ", visited.Select(s => s?.Alias ?? "?"));

            var approve = visited.FindIndex(s => s?.ActionAlias == "umbracoAutomate.requestApproval");
            if (approve < 0 || path[approve + 1].Outcome != "approved")
            {
                return "no approved Request Approval on " + describe;
            }

            var firstFind = visited.FindIndex(s => s?.ActionAlias == "community.jira.findContentByKey");
            var findAgain = visited.FindIndex(approve + 1, s => s?.ActionAlias == "community.jira.findContentByKey");
            if (firstFind < 0 || firstFind > approve || findAgain < 0)
            {
                return "no Find before and after approval on " + describe;
            }

            var first = visited[firstFind]!.Alias;
            var again = visited[findAgain]!.Alias;
            var recheck = visited.FindIndex(findAgain + 1, s => s is not null && IsRecheck(s, first, again));
            if (recheck < 0 || recheck >= visited.Count - 1 || path[recheck + 1].Outcome != "true")
            {
                return "no exact UpdateDate and ContentKey recheck before publish on " + describe;
            }

            // The publish uses the rechecked node, never a key from before the approval.
            if (visited[^1]!.Settings.GetProperty("contentKey").GetString() != $"${{ steps.{again}.contentKey }}")
            {
                return "publish does not use the rechecked key on " + describe;
            }
        }

        return null;
    }

    [Fact]
    public void UseCase4Seed_HasStatusChangeConditionBeforeFind()
    {
        var (steps, edges) = Graph(Seed());
        var read = steps.Values.Single(s => s.ActionAlias == "community.jira.readWebhookEvent").Alias;
        var paths = PathsTo(steps, edges, "community.jira.findContentByKey");

        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            var visited = path.Select(e => steps.GetValueOrDefault(e.Target)).ToList();
            var condition = visited.FindIndex(s => s is not null && IsStatusChangeCheck(s, read));
            Assert.True(condition >= 0, "No exact status-change condition before Find");
            Assert.Equal("true", path[condition + 1].Outcome);
        }
    }

    // SEC-M3-2: the approver sees which node (its key and type), not just its editable name.
    [Fact]
    public void UseCase4Seed_ApprovalPromptNamesTheNodeKey()
    {
        var (steps, _) = Graph(Seed());
        var approve = steps.Values.Single(s => s.ActionAlias == "umbracoAutomate.requestApproval");
        var find = steps.Values.First(s => s.ActionAlias == "community.jira.findContentByKey").Alias;
        var prompt = approve.Settings.GetProperty("prompt").GetString()!;

        Assert.Contains($"${{ steps.{find}.contentKey }}", prompt, StringComparison.Ordinal);
        Assert.Contains($"${{ steps.{find}.contentTypeAlias }}", prompt, StringComparison.Ordinal);
    }

    // QA-M3-4: any action the F-01 walk does not model (Start Automation, Run Script, Create Content ...) forces a
    // review of this test before it can enter the recipe.
    [Fact]
    public void UseCase4Seed_UsesOnlyModelledActions()
    {
        string[] allowed =
        [
            "community.jira.readWebhookEvent", "umbracoAutomate.if", "community.jira.findContentByKey",
            "umbracoAutomate.logMessage", "umbracoAutomate.requestApproval", "umbracoAutomate.publishContent",
        ];
        var (steps, _) = Graph(Seed());

        Assert.All(steps.Values, s => Assert.Contains(s.ActionAlias, allowed));
    }

    // QA-M3-3: both Find steps search exactly the type the demo seeder creates (UseCase4Seeder.ArticleTypeKey).
    [Fact]
    public void UseCase4Seed_FindStepsSearchTheSeededArticleType()
    {
        var (steps, _) = Graph(Seed());

        Assert.All(
            steps.Values.Where(s => s.ActionAlias == "community.jira.findContentByKey"),
            s => Assert.Equal("6a1e0c2b-3d4f-4a5b-8c6d-7e8f9a0b1c2d", s.Settings.GetProperty("contentTypes").GetString()));
    }

    [Fact]
    public void UseCase4Seed_UsesSignatureAuthenticator()
    {
        var authenticator = Seed().GetProperty("automation").GetProperty("trigger").GetProperty("settings").GetProperty("authenticator");

        Assert.Equal("community.jira.signature", authenticator.GetProperty("alias").GetString());
    }

    [Fact]
    public void UseCase4Seed_NeverUsesNameMatch()
    {
        var (steps, _) = Graph(Seed());

        Assert.DoesNotContain(steps.Values, s => s.ActionAlias == "umbracoAutomate.findContent");
    }
}
