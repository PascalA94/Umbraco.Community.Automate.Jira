using System.Text.RegularExpressions;

namespace Umbraco.Community.Automate.Jira.Tests;

/// <summary>
/// Pins the README's use case 4 section (F-01, plan review P1). The README is the package's NuGet readme too
/// (check-packed-readme.sh), so these warnings reach every installer. Publishing from Jira is only ever documented
/// behind its warning and with Request Approval.
/// </summary>
public class ReadmeTests
{
    private const string AdvancedHeading = "Advanced: Jira-driven publishing";
    private const string WarningText = "Anyone who can move a Jira issue into this status";

    private static string[] Lines() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Docs", "README.md"));

    private static bool IsHeading(string line) => Regex.IsMatch(line, @"^#{1,6} ");

    private static (int Start, int End) AdvancedSection(string[] lines)
    {
        var starts = lines.Select((l, i) => (l, i))
            .Where(x => IsHeading(x.l) && x.l.TrimStart('#', ' ').StartsWith(AdvancedHeading, StringComparison.Ordinal))
            .Select(x => x.i)
            .ToList();
        Assert.Single(starts);

        var start = starts[0];
        var level = lines[start].TakeWhile(c => c == '#').Count();
        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (IsHeading(lines[i]) && lines[i].TakeWhile(c => c == '#').Count() <= level)
            {
                end = i;
                break;
            }
        }

        return (start, end);
    }

    private static bool IsNumberedStep(string line) => Regex.IsMatch(line, @"^\d+\. ");

    [Fact]
    public void Readme_UseCase4_WarningPrecedesRecipe()
    {
        var lines = Lines();
        var (start, end) = AdvancedSection(lines);

        var firstBlock = Enumerable.Range(start + 1, end - start - 1).First(i => !string.IsNullOrWhiteSpace(lines[i]));
        Assert.StartsWith(">", lines[firstBlock], StringComparison.Ordinal);

        var quote = string.Join(" ", lines.Skip(firstBlock).TakeWhile(l => l.StartsWith('>')).Select(l => l.TrimStart('>', ' ')));
        Assert.Contains(WarningText, quote, StringComparison.Ordinal);

        var firstStep = Enumerable.Range(start, end - start).First(i => IsNumberedStep(lines[i]));
        Assert.True(firstBlock < firstStep, "The warning must come before the first numbered recipe step.");
    }

    [Fact]
    public void Readme_PublishContent_OnlyInsideAdvancedSection()
    {
        var lines = Lines();
        var (start, end) = AdvancedSection(lines);

        for (var i = 0; i < lines.Length; i++)
        {
            if (i >= start && i < end)
            {
                continue;
            }

            Assert.DoesNotContain("Publish Content", lines[i], StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("publishContent", lines[i], StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Readme_UseCase4Recipe_ContainsRequestApproval()
    {
        var lines = Lines();
        var (start, end) = AdvancedSection(lines);
        var steps = lines[start..end].Where(IsNumberedStep).ToList();

        var approval = steps.FindIndex(s => s.Contains("Request Approval", StringComparison.Ordinal));
        var publish = steps.FindIndex(s => s.Contains("Publish Content", StringComparison.Ordinal));
        Assert.True(approval >= 0, "No numbered step names Request Approval.");
        Assert.True(publish >= 0, "No numbered step names Publish Content.");
        Assert.True(approval < publish, "Request Approval must come before Publish Content.");
    }
}
