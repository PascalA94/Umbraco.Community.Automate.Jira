using System.Text.RegularExpressions;

namespace Umbraco.Community.Automate.Jira.Tests;

// Fixtures are committed to a public repo, so any personal data in them must be invented
// (security review F-06). Every email, Atlassian account ID and displayName found under
// Fixtures/ has to be listed in Fixtures/synthetic-allowlist.txt.
public sealed partial class FixtureScannerTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void Fixtures_ContainOnlySyntheticPersonalData()
    {
        var violations = Scan(FixtureRoot, LoadAllowList());

        // Names the file and line only, never the value, so a real address is not echoed into CI logs.
        Assert.True(violations.Count == 0, "Non-synthetic personal data in fixtures:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Scanner_FlagsUnlistedEmail()
    {
        // Guards the scanner itself: an unlisted, real-looking address must be reported.
        var dir = Directory.CreateTempSubdirectory("fixture-scan-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "scratch.json"), "{\n  \"emailAddress\": \"someone@realcompany.co.uk\"\n}\n");

            var violations = Scan(dir.FullName, LoadAllowList());

            Assert.Equal(["scratch.json:2 email"], violations);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scanner_FlagsUnlistedAccountIdAndDisplayName()
    {
        var dir = Directory.CreateTempSubdirectory("fixture-scan-");
        try
        {
            File.WriteAllText(
                Path.Combine(dir.FullName, "scratch.json"),
                "{\n  \"accountId\": \"5b10ac8d82e05b22cc7d4ef5\",\n  \"displayName\": \"Real Person\"\n}\n");

            var violations = Scan(dir.FullName, LoadAllowList());

            Assert.Equal(["scratch.json:2 account ID", "scratch.json:3 displayName"], violations);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scanner_FlagsDisplayNameAndHostInsideEscapedBody()
    {
        // issue_updated.signed.json keeps the delivery body as a JSON string, so names and hosts
        // inside it are escaped. A scrubbed real capture will land in exactly this wrapper.
        var dir = Directory.CreateTempSubdirectory("fixture-scan-");
        try
        {
            File.WriteAllText(
                Path.Combine(dir.FullName, "scratch.json"),
                "{\n  \"body\": \"{\\\"displayName\\\":\\\"Real Person\\\",\\\"self\\\":\\\"https://realtenant.atlassian.net/x\\\"}\"\n}\n");

            var violations = Scan(dir.FullName, LoadAllowList());

            Assert.Equal(["scratch.json:2 displayName", "scratch.json:2 Atlassian host"], violations);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static HashSet<string> LoadAllowList() =>
        File.ReadAllLines(Path.Combine(FixtureRoot, "synthetic-allowlist.txt"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

    private static List<string> Scan(string root, HashSet<string> allowed)
    {
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == "synthetic-allowlist.txt")
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                Check(Email(), "email");
                Check(AccountId24(), "account ID");
                Check(AccountIdPrefixed(), "account ID");
                Check(DisplayName(), "displayName", group: 1);
                Check(AtlassianHost(), "Atlassian host");

                void Check(Regex pattern, string kind, int group = 0)
                {
                    foreach (Match m in pattern.Matches(lines[i]))
                    {
                        if (!allowed.Contains(m.Groups[group].Value))
                        {
                            violations.Add($"{relative}:{i + 1} {kind}");
                        }
                    }
                }
            }
        }

        return violations;
    }

    [GeneratedRegex(@"[^\s""@\\]+@[^\s""@\\]+\.[^\s""@\\]+")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b[0-9a-f]{24}\b")]
    private static partial Regex AccountId24();

    [GeneratedRegex(@"\b\d{6}:[0-9a-f-]{36}\b")]
    private static partial Regex AccountIdPrefixed();

    // Tolerates the backslash-escaped quotes of a JSON body held as a string.
    [GeneratedRegex(@"\\?""displayName\\?""\s*:\s*\\?""([^""\\]*)\\?""")]
    private static partial Regex DisplayName();

    [GeneratedRegex(@"[a-z0-9-]+\.atlassian\.net")]
    private static partial Regex AtlassianHost();
}
