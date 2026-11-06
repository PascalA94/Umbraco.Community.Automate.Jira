using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class PlainTextAdfConverterTests
{
    private static AdfResult Convert(string? input)
    {
        var result = new PlainTextAdfConverter().Convert(input);
        if (result.Document is not null)
        {
            AdfSchemaAssert.Valid(result.Document);
        }

        return result;
    }

    private static JsonArray Content(AdfResult result) => result.Document!["content"]!.AsArray();

    [Fact]
    public void BlankLine_MakesTwoParagraphs()
    {
        var content = Content(Convert("a\n\nb"));

        Assert.Equal(2, content.Count);
        Assert.All(content, p => Assert.Equal("paragraph", (string?)p!["type"]));
        Assert.Equal("a", (string?)content[0]!["content"]![0]!["text"]);
        Assert.Equal("b", (string?)content[1]!["content"]![0]!["text"]);
    }

    [Fact]
    public void SingleNewline_IsHardBreak()
    {
        var paragraph = Assert.Single(Content(Convert("a\nb")))!;

        var inline = paragraph["content"]!.AsArray();
        Assert.Equal(["text", "hardBreak", "text"], inline.Select(n => (string?)n!["type"]));
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public void CarriageReturns_TreatedAsNewline(string input)
    {
        var paragraph = Assert.Single(Content(Convert(input)))!;

        Assert.Equal(["text", "hardBreak", "text"], paragraph["content"]!.AsArray().Select(n => (string?)n!["type"]));
    }

    [Fact]
    public void SeveralBlankLines_AreOneBreak()
    {
        Assert.Equal(2, Content(Convert("a\r\n\r\n  \n\nb")).Count);
    }

    [Fact]
    public void MarkdownAndLinks_AreLiteralText()
    {
        const string input = "# not a heading [x](javascript:alert(1))";

        var paragraph = Assert.Single(Content(Convert(input)))!;

        var text = Assert.Single(paragraph["content"]!.AsArray())!;
        Assert.Equal(input, (string?)text["text"]);
        Assert.Null(text["marks"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\r\n \t")]
    public void Blank_GivesNoDocument(string? input)
    {
        var result = Convert(input);

        Assert.Null(result.Document);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Document_HasDocRoot()
    {
        var doc = Convert("x").Document!;

        Assert.Equal("doc", (string?)doc["type"]);
        Assert.Equal(1, (int?)doc["version"]);
    }

    [Fact]
    public void OverCap_TruncatedWithSuffix()
    {
        var result = Convert(new string('a', AdfLimits.MaxTextLength + 10));

        var text = (string)Content(result)[0]!["content"]![0]!["text"]!;
        Assert.EndsWith(" (truncated)", text);
        Assert.Equal(AdfLimits.MaxTextLength, text.Length);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Factory_GivesPlainText()
    {
        var provider = TestHost.Compose();

        var factory = provider.GetRequiredService<IAdfConverterFactory>();

        Assert.IsType<PlainTextAdfConverter>(factory.For(JiraTextFormat.PlainText));
    }
}
