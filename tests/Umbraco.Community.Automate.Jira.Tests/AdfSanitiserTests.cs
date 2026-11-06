using System.Text.Json.Nodes;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class AdfSanitiserTests
{
    private static (JsonObject Doc, List<string> Warnings) Sanitise(string json)
    {
        var doc = JsonNode.Parse(json)!.AsObject();
        var warnings = new List<string>();
        AdfSanitiser.Sanitise(doc, warnings);
        AdfSchemaAssert.Valid(doc);
        return (doc, warnings);
    }

    private static string Doc(params string[] blocks) => $$"""{"type":"doc","version":1,"content":[{{string.Join(",", blocks)}}]}""";

    private static string Para(params string[] inline) => $$"""{"type":"paragraph","content":[{{string.Join(",", inline)}}]}""";

    private static string Text(string text, string? marks = null) =>
        marks is null ? $$"""{"type":"text","text":"{{text}}"}""" : $$"""{"type":"text","text":"{{text}}","marks":[{{marks}}]}""";

    private static string Link(string href, string extra = "") => $$$"""{"type":"link","attrs":{"href":"{{{href}}}"{{{extra}}}}}""";

    private static IEnumerable<JsonObject> Texts(JsonNode node) =>
        node.AsObject() is { } obj && (string?)obj["type"] == "text"
            ? [obj]
            : (node["content"] as JsonArray ?? []).SelectMany(c => Texts(c!));

    private static string AllText(JsonNode node) => string.Concat(Texts(node).Select(t => (string?)t["text"]));

    private static IEnumerable<string> Types(JsonNode node) =>
        new[] { (string)node["type"]! }.Concat((node["content"] as JsonArray ?? []).SelectMany(c => Types(c!)));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ftp://x.example")]
    public void Link_DisallowedScheme_RemovedTextKept(string href)
    {
        var (doc, warnings) = Sanitise(Doc(Para(Text("click", Link(href)))));

        var text = Assert.Single(Texts(doc));
        Assert.Equal("click", (string?)text["text"]);
        Assert.Null(text["marks"]);
        Assert.NotEmpty(warnings);
        Assert.DoesNotContain(warnings, w => w.Contains(href, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("mailto:a@example.com")]
    [InlineData("https://example.com/a?b=c")]
    [InlineData("http://example.com")]
    public void Link_AllowedScheme_Kept(string href)
    {
        var (doc, warnings) = Sanitise(Doc(Para(Text("x", Link(href)))));

        var mark = Assert.Single(Texts(doc).Single()["marks"]!.AsArray())!;
        Assert.Equal("link", (string?)mark["type"]);
        Assert.Equal(href, (string?)mark["attrs"]!["href"]);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("java\\tscript:x")]
    [InlineData("/relative")]
    [InlineData(" https://x\\u0000")]
    [InlineData("https://x\\u0007y")]
    [InlineData("")]
    public void Sanitiser_Href_WithControlCharsOrRelative_Dropped(string href)
    {
        var (doc, warnings) = Sanitise(Doc(Para(Text("x", Link(href)))));

        Assert.Null(Texts(doc).Single()["marks"]);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void Link_HrefTrimmed()
    {
        var (doc, _) = Sanitise(Doc(Para(Text("x", Link("  https://a.example  ")))));

        Assert.Equal("https://a.example", (string?)Texts(doc).Single()["marks"]![0]!["attrs"]!["href"]);
    }

    [Fact]
    public void Sanitiser_LinkMark_ExtraAttrsDropped()
    {
        var (doc, _) = Sanitise(Doc(Para(Text("x", Link("https://a.example", ""","title":"t","id":"i","collection":"c" """)))));

        var attrs = Texts(doc).Single()["marks"]![0]!["attrs"]!.AsObject();
        Assert.Equal(["href"], attrs.Select(p => p.Key));
    }

    [Fact]
    public void Sanitiser_UnknownMark_Dropped()
    {
        var marks = string.Join(",", new[] { "underline", "textColor", "subsup", "backgroundColor", "futureMark" }
            .Select(m => "{\"type\":\"" + m + "\",\"attrs\":{\"color\":\"#ff0000\",\"type\":\"sub\"}}"));

        var (doc, warnings) = Sanitise(Doc(Para(Text("kept", marks + """,{"type":"strong"}"""))));

        var text = Texts(doc).Single();
        Assert.Equal("kept", (string?)text["text"]);
        Assert.Equal(["strong"], text["marks"]!.AsArray().Select(m => (string?)m!["type"]));
        Assert.Equal(5, warnings.Count);
    }

    [Fact]
    public void Marks_AllowedOnesKept()
    {
        var (doc, warnings) = Sanitise(Doc(Para(Text("x", """{"type":"strong"},{"type":"em"},{"type":"strike"}"""))));

        Assert.Equal(["strong", "em", "strike"], Texts(doc).Single()["marks"]!.AsArray().Select(m => (string?)m!["type"]));
        Assert.Empty(warnings);
    }

    [Fact]
    public void CodeMark_KeepsOnlyCodeAndLink()
    {
        // The schema allows code only alongside link.
        var (doc, _) = Sanitise(Doc(Para(Text("x", """{"type":"strong"},{"type":"code"},""" + Link("https://a.example")))));

        Assert.Equal(["code", "link"], Texts(doc).Single()["marks"]!.AsArray().Select(m => (string?)m!["type"]));
    }

    [Fact]
    public void Extension_Unwrapped_WithWarning()
    {
        var extension = """{"type":"extension","attrs":{"extensionType":"com.evil","extensionKey":"k","text":"ext text"}}""";

        var (doc, warnings) = Sanitise(Doc(Para(Text("a")), extension));

        Assert.DoesNotContain("extension", Types(doc));
        Assert.Contains("ext text", AllText(doc));
        Assert.Contains(warnings, w => w.Contains("extension", StringComparison.Ordinal));
    }

    [Fact]
    public void Sanitiser_Allowlist_UnwrapsInlineCardBlockCardEmbedCardMention()
    {
        var doc = Doc(
            Para(
                Text("see "),
                """{"type":"inlineCard","attrs":{"url":"https://card.example"}}""",
                Text(" by "),
                """{"type":"mention","attrs":{"id":"acc-1","text":"@Ann"}}"""),
            """{"type":"blockCard","attrs":{"url":"https://block.example"}}""",
            """{"type":"embedCard","attrs":{"url":"https://embed.example","layout":"center"}}""");

        var (result, warnings) = Sanitise(doc);

        var types = Types(result).ToList();
        Assert.DoesNotContain("inlineCard", types);
        Assert.DoesNotContain("blockCard", types);
        Assert.DoesNotContain("embedCard", types);
        Assert.DoesNotContain("mention", types);
        Assert.Contains("@Ann", AllText(result));
        Assert.Equal(4, warnings.Count);
    }

    [Fact]
    public void Sanitiser_UnknownNodeType_Unwrapped()
    {
        var (doc, warnings) = Sanitise(Doc("""{"type":"futureThing","content":[{"type":"text","text":"x"}]}"""));

        Assert.Equal("x", AllText(doc));
        Assert.DoesNotContain("futureThing", Types(doc));
        Assert.Single(warnings);
    }

    [Fact]
    public void Sanitiser_UnknownBlockWithBlocks_KeepsTheBlocks()
    {
        var (doc, _) = Sanitise(Doc("""{"type":"panel","attrs":{"panelType":"info"},"content":[""" + Para(Text("in panel")) + "]}"));

        var paragraph = Assert.Single(doc["content"]!.AsArray())!;
        Assert.Equal("paragraph", (string?)paragraph["type"]);
        Assert.Equal("in panel", AllText(paragraph));
    }

    [Fact]
    public void Sanitiser_StrangeTypeName_NotEchoedRaw()
    {
        var (_, warnings) = Sanitise(Doc("""{"type":"<script>alert(1)</script>","content":[{"type":"text","text":"x"}]}"""));

        Assert.DoesNotContain(warnings, w => w.Contains('<', StringComparison.Ordinal));
    }

    [Fact]
    public void Attributes_OnlyAllowlisted()
    {
        var (doc, _) = Sanitise(Doc(
            """{"type":"heading","attrs":{"level":2,"localId":"x"},"content":[{"type":"text","text":"h"}]}""",
            """{"type":"orderedList","attrs":{"order":3,"localId":"y"},"content":[{"type":"listItem","content":[""" + Para(Text("i")) + "]}]}",
            """{"type":"codeBlock","attrs":{"language":"c#","uniqueId":"z"},"content":[{"type":"text","text":"code"}]}""",
            """{"type":"paragraph","attrs":{"localId":"p"},"content":[{"type":"text","text":"p"}]}"""));

        var blocks = doc["content"]!.AsArray();
        Assert.Equal(["level"], blocks[0]!["attrs"]!.AsObject().Select(p => p.Key));
        Assert.Equal(["order"], blocks[1]!["attrs"]!.AsObject().Select(p => p.Key));
        Assert.Equal(3, (int?)blocks[1]!["attrs"]!["order"]);
        Assert.Equal(["language"], blocks[2]!["attrs"]!.AsObject().Select(p => p.Key));
        Assert.Null(blocks[3]!["attrs"]);
    }

    [Theory]
    [InlineData("""
        "a b"
        """)]
    [InlineData("""
        "x<script>"
        """)]
    [InlineData("""
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        """)]
    [InlineData("3")]
    public void CodeBlockLanguage_Invalid_Dropped(string language)
    {
        var (doc, _) = Sanitise(Doc($$"""{"type":"codeBlock","attrs":{"language":{{language}}},"content":[{"type":"text","text":"c"}]}"""));

        Assert.Null(doc["content"]![0]!["attrs"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("\"2\"")]
    [InlineData("2.5")]
    public void HeadingLevel_Invalid_BecomesParagraph(string level)
    {
        var (doc, _) = Sanitise(Doc($$"""{"type":"heading","attrs":{"level":{{level}}},"content":[{"type":"text","text":"h"}]}"""));

        Assert.Equal("paragraph", (string?)doc["content"]![0]!["type"]);
        Assert.Equal("h", AllText(doc));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"3\"")]
    public void OrderedListOrder_Invalid_Dropped(string order)
    {
        var (doc, _) = Sanitise(Doc($$"""{"type":"orderedList","attrs":{"order":{{order}}},"content":[{"type":"listItem","content":[{{Para(Text("i"))}}]}]}"""));

        Assert.Null(doc["content"]![0]!["attrs"]);
    }

    [Fact]
    public void ContentModel_HeadingAndRuleInsideListOrQuote_StaySchemaValid()
    {
        var heading = """{"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"h"}]}""";
        var rule = """{"type":"rule"}""";
        var quote = $$"""{"type":"blockquote","content":[{{heading}},{{rule}},{"type":"blockquote","content":[{{Para(Text("inner"))}}]}]}""";
        var list = $$"""{"type":"bulletList","content":[{"type":"listItem","content":[{{heading}},{{rule}},{{quote}}]},{{Para(Text("loose"))}}]}""";

        var (doc, _) = Sanitise(Doc(quote, list));

        Assert.Contains("inner", AllText(doc));
        Assert.Contains("loose", AllText(doc));
    }

    [Fact]
    public void ContentModel_EmptyContainers_Dropped()
    {
        var (doc, _) = Sanitise(Doc(
            """{"type":"bulletList","content":[]}""",
            """{"type":"blockquote","content":[]}""",
            """{"type":"paragraph","content":[{"type":"text","text":""}]}""",
            Para(Text("x"))));

        Assert.Equal(["paragraph", "paragraph"], doc["content"]!.AsArray().Select(b => (string?)b!["type"]));
    }

    [Fact]
    public void ContentModel_InlineAtBlockLevel_WrappedInParagraph()
    {
        var (doc, _) = Sanitise(Doc(Text("bare"), """{"type":"hardBreak"}""", Text("more")));

        var paragraph = Assert.Single(doc["content"]!.AsArray())!;
        Assert.Equal("paragraph", (string?)paragraph["type"]);
        Assert.Equal("baremore", AllText(paragraph));
    }

    [Fact]
    public void CodeBlock_ContentIsPlainText()
    {
        var (doc, _) = Sanitise(Doc("""{"type":"codeBlock","content":[{"type":"text","text":"a","marks":[{"type":"strong"}]},{"type":"hardBreak"},{"type":"text","text":"b"}]}"""));

        var text = Assert.Single(Texts(doc));
        Assert.Equal("a\nb", (string?)text["text"]);
        Assert.Null(text["marks"]);
    }

    [Fact]
    public void Root_ExtraPropertiesRemovedAndVersionSet()
    {
        var (doc, _) = Sanitise("""{"type":"doc","version":1,"extra":true,"content":[{"type":"paragraph"}]}""");

        Assert.Equal(["type", "version", "content"], doc.Select(p => p.Key));
    }

    [Fact]
    public void Truncate_TotalTextOverCap_EndsWithSuffix()
    {
        var half = new string('a', (AdfLimits.MaxTextLength / 2) + 100);

        var (doc, warnings) = Sanitise(Doc(Para(Text(half)), Para(Text(half)), Para(Text("after"))));

        var all = AllText(doc);
        Assert.EndsWith(" (truncated)", all);
        Assert.Equal(AdfLimits.MaxTextLength, all.Length);
        Assert.DoesNotContain("after", all);
        Assert.Contains(warnings, w => w.Contains("cut", StringComparison.Ordinal));
    }

    [Fact]
    public void Truncate_DoesNotSplitSurrogatePair()
    {
        var budget = AdfLimits.MaxTextLength - " (truncated)".Length;
        var text = new string('a', budget - 1) + "😀" + "tail";

        var cut = AdfLimits.TruncateOnCodePoint(text, budget);

        Assert.Equal(budget - 1, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));

        var (doc, _) = Sanitise(Doc(Para(Text(text))));
        var all = AllText(doc);
        Assert.False(HasLoneSurrogate(all));
        Assert.EndsWith(" (truncated)", all);
    }

    [Theory]
    [InlineData("abc", 5, "abc")]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("ab😀", 3, "ab")]
    [InlineData("ab😀", 4, "ab😀")]
    public void TruncateOnCodePoint_Cases(string input, int max, string expected)
    {
        Assert.Equal(expected, AdfLimits.TruncateOnCodePoint(input, max));
    }

    [Fact]
    public void DeepNesting_Flattened_NoCrash()
    {
        var node = Para(Text("deep"));
        for (var i = 0; i < 60; i++)
        {
            node = $$"""{"type":"bulletList","content":[{"type":"listItem","content":[{{node}}]}]}""";
        }

        var json = Doc(node);
        var doc = JsonNode.Parse(json, documentOptions: new System.Text.Json.JsonDocumentOptions { MaxDepth = 1000 })!.AsObject();
        var warnings = new List<string>();

        AdfSanitiser.Sanitise(doc, warnings);

        AdfSchemaAssert.Valid(doc);
        Assert.Equal("deep", AllText(doc));
        Assert.True(Depth(doc) <= 2 * (AdfLimits.MaxNesting + 2), $"depth {Depth(doc)}");
    }

    private static bool HasLoneSurrogate(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(s[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static int Depth(JsonNode node) => 1 + (node["content"] as JsonArray ?? []).Select(c => Depth(c!)).DefaultIfEmpty(0).Max();
}
