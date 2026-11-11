using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Meziantou.Framework.AtlassianDataFormat;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class MarkdownAdfConverterTests
{
    // Set JIRA_UPDATE_GOLDENS=1 to rewrite the golden files from the current output; review the diff before keeping it.
    private static readonly bool UpdateGoldens = Environment.GetEnvironmentVariable("JIRA_UPDATE_GOLDENS") == "1";

    public static TheoryData<string> Goldens() =>
    [
        "paragraph", "headings", "bullet-list", "ordered-list", "nested-list", "code-block", "blockquote", "rule", "marks", "unsupported",
    ];

    private static AdfResult Convert(string markdown)
    {
        var result = new MarkdownAdfConverter().Convert(markdown);
        if (result.Document is not null)
        {
            AdfSchemaAssert.Valid(result.Document);
        }

        return result;
    }

    private static IEnumerable<JsonObject> Nodes(JsonNode node) =>
        new[] { node.AsObject() }.Concat((node["content"] as JsonArray ?? []).SelectMany(c => Nodes(c!)));

    private static IEnumerable<JsonObject> Marks(JsonNode doc) =>
        Nodes(doc).SelectMany(n => (n["marks"] as JsonArray ?? []).Select(m => m!.AsObject()));

    private static string AllText(JsonNode doc) => string.Concat(Nodes(doc).Select(n => (string?)n["text"]));

    private static string SourceFixtures([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(path)!, "Fixtures", "adf");

    [Theory]
    [MemberData(nameof(Goldens))]
    public void Golden(string name)
    {
        var markdown = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "adf", name + ".md"));

        var doc = Convert(markdown).Document!;

        // Goldens are read from the output folder: CI builds with ContinuousIntegrationBuild, which maps
        // [CallerFilePath] to "/_/…", so the source path only exists on a developer machine. Rewriting them
        // (JIRA_UPDATE_GOLDENS=1, local only) writes the source copy and reads it back.
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "adf", name + ".golden.json");
        if (UpdateGoldens)
        {
            goldenPath = Path.Combine(SourceFixtures(), name + ".golden.json");
            File.WriteAllText(goldenPath, doc.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        }

        var golden = JsonNode.Parse(File.ReadAllText(goldenPath));
        Assert.True(JsonNode.DeepEquals(golden, JsonNode.Parse(doc.ToJsonString())), $"{name} differs from its golden file:\n{doc.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(Goldens))]
    public void Oracle_MeziantouMarkdownKeepsTheText(string name)
    {
        var markdown = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "adf", name + ".md"));
        var doc = Convert(markdown).Document!;

        // An independent ADF reader turns our output back into Markdown; every word of the source text must survive.
        // It escapes Markdown punctuation in text, so backslashes are removed before comparing.
        var roundTrip = AdfDocument.Parse(doc.ToJsonString()).ToMarkdown().Replace("\\", string.Empty, StringComparison.Ordinal);

        var words = Nodes(doc).Select(n => (string?)n["text"]).OfType<string>().SelectMany(t => t.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries));
        foreach (var word in words)
        {
            Assert.Contains(word, roundTrip, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Structure_HeadingsListsCodeQuoteRule()
    {
        var doc = Convert("# H\n\n- a\n- b\n\n5. x\n\n```js\ncode\n```\n\n> q\n\n---\n").Document!;

        var types = doc["content"]!.AsArray().Select(b => (string?)b!["type"]).ToList();
        Assert.Equal(["heading", "bulletList", "orderedList", "codeBlock", "blockquote", "rule"], types);
        Assert.Equal(1, (int?)doc["content"]![0]!["attrs"]!["level"]);
        Assert.Equal(5, (int?)doc["content"]![2]!["attrs"]!["order"]);
        Assert.Equal("js", (string?)doc["content"]![3]!["attrs"]!["language"]);
    }

    [Fact]
    public void Marks_AllFive()
    {
        var doc = Convert("**s** *e* ~~k~~ `c` [l](https://x.example)").Document!;

        Assert.Equal(["strong", "em", "strike", "code", "link"], Marks(doc).Select(m => (string?)m["type"]).ToList());
    }

    [Fact]
    public void Unsupported_TableImageHtml_BecomeText_NoMedia()
    {
        var doc = Convert("| a | b |\n|---|---|\n| 1 | 2 |\n\n![alt](https://img.example/x.png)\n\n<script>alert(1)</script>").Document!;

        var types = Nodes(doc).Select(n => (string?)n["type"]).ToHashSet();
        Assert.DoesNotContain("media", types);
        Assert.DoesNotContain("mediaSingle", types);
        Assert.DoesNotContain("table", types);
        Assert.Contains("<script>alert(1)</script>", AllText(doc));
    }

    [Fact]
    public void JavascriptLink_TextKeptWithoutLink()
    {
        var doc = Convert("[x](javascript:alert(1))").Document!;

        Assert.Equal("x", AllText(doc));
        Assert.Empty(Marks(doc));
    }

    [Theory]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("[x][r]\n\n[r]: javascript:alert(1)")]
    [InlineData("[x](data:text/html,a)")]
    [InlineData("[x]( javascript:alert(1))")]
    [InlineData("[x](<javascript:alert(1)>)")]
    [InlineData("[x](JAVASCRIPT:alert(1))")]
    public void Markdown_DisallowedLinkForms_NoLinkMark(string markdown)
    {
        var doc = Convert(markdown).Document;

        Assert.True(doc is null || !Marks(doc).Any(m => (string?)m["type"] == "link"));
    }

    [Fact]
    public void Markdown_WwwAutolink_IsPlainText()
    {
        var doc = Convert("see www.evil.example now").Document!;

        Assert.Empty(Marks(doc));
        Assert.Equal("see www.evil.example now", AllText(doc));
    }

    [Fact]
    public void Markdown_EmailAutolink_IsMailto()
    {
        var doc = Convert("<ann@example.com>").Document!;

        Assert.Equal("mailto:ann@example.com", (string?)Assert.Single(Marks(doc))["attrs"]!["href"]);
    }

    [Fact]
    public void Markdown_LinkTitle_Dropped()
    {
        var doc = Convert("[x](https://a.example \"t\")").Document!;

        var attrs = Assert.Single(Marks(doc))["attrs"]!.AsObject();
        Assert.Equal(["href"], attrs.Select(p => p.Key));
        Assert.Equal("https://a.example", (string?)attrs["href"]);
    }

    [Fact]
    public void Markdown_Over64KB_FailsValidation()
    {
        // Multi-byte characters: the cap is on UTF-8 bytes, not characters.
        var input = new string('é', (AdfLimits.MaxInputBytes / 2) + 1);
        Assert.True(Encoding.UTF8.GetByteCount(input) > AdfLimits.MaxInputBytes);

        Assert.Throws<AdfInputTooLargeException>(() => new MarkdownAdfConverter().Convert(input));
    }

    [Fact]
    public void Markdown_AtLimit_Converts()
    {
        var input = new string('a', AdfLimits.MaxInputBytes);

        Assert.NotNull(Convert(input).Document);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Blank_GivesNoDocument(string? input)
    {
        Assert.Null(Convert(input!).Document);
    }

    [Fact]
    public void Markdown_Nested200Levels_DoesNotCrash()
    {
        var quotes = new string('>', 200) + " deep quote";
        var lists = string.Concat(Enumerable.Range(0, 200).Select(i => new string(' ', i * 2) + "- item" + i + "\n"));
        AdfResult? quoteResult = null;
        AdfResult? listResult = null;
        Exception? failure = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    quoteResult = new MarkdownAdfConverter().Convert(quotes);
                    listResult = new MarkdownAdfConverter().Convert(lists);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            maxStackSize: 256 * 1024);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "conversion did not finish");

        Assert.Null(failure);
        AdfSchemaAssert.Valid(quoteResult!.Document);
        AdfSchemaAssert.Valid(listResult!.Document);
        Assert.Contains("deep quote", AllText(quoteResult.Document!));
        Assert.Contains("item0", AllText(listResult.Document!)); // ~40k characters of indentation: the text cap applies too.
        Assert.True(Depth(quoteResult.Document!) <= (2 * AdfLimits.MaxNesting) + 4);
        Assert.True(Depth(listResult.Document!) <= (2 * AdfLimits.MaxNesting) + 4);
    }

    [Fact]
    public void Markdown_TooDeepForMarkdig_FallsBackToPlainTextWithWarning()
    {
        var result = Convert(new string('>', 200) + " deep quote");

        Assert.Contains("deep quote", AllText(result.Document!));
        Assert.Contains(result.Warnings, w => w.Contains("plain text", StringComparison.Ordinal));
    }

    [Fact]
    public void Factory_GivesMarkdown()
    {
        var factory = TestHost.Compose().GetRequiredService<IAdfConverterFactory>();

        Assert.IsType<MarkdownAdfConverter>(factory.For(JiraTextFormat.Markdown));
    }

    private static int Depth(JsonNode node) => 1 + (node["content"] as JsonArray ?? []).Select(c => Depth(c!)).DefaultIfEmpty(0).Max();
}
