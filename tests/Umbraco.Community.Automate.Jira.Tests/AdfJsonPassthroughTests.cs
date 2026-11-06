using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Community.Automate.Jira.Adf;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class AdfJsonPassthroughTests
{
    private const string Valid = """{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"hi","marks":[{"type":"strong"}]}]}]}""";

    [Fact]
    public void ValidDoc_UnchangedExceptSanitised()
    {
        var result = new AdfJsonPassthrough().Convert(Valid);

        AdfSchemaAssert.Valid(result.Document);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Valid), JsonNode.Parse(result.Document!.ToJsonString())));
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("""{"type":"doc","version":2,"content":[]}""")]
    [InlineData("""{"type":"doc","version":"1","content":[]}""")]
    [InlineData("""{"type":"doc","version":1}""")]
    [InlineData("""{"type":"doc","version":1,"content":{}}""")]
    [InlineData("""{"type":"paragraph","version":1,"content":[]}""")]
    [InlineData("""[{"type":"doc","version":1,"content":[]}]""")]
    [InlineData("not json")]
    [InlineData("""{"type":"doc","version":1,"content":[]""")]
    [InlineData("\"doc\"")]
    public void BadShape_Throws(string input)
    {
        Assert.Throws<AdfFormatException>(() => new AdfJsonPassthrough().Convert(input));
    }

    [Fact]
    public void Nested40Deep_ThrowsFormat()
    {
        var inner = """{"type":"text","text":"x"}""";
        for (var i = 0; i < 40; i++)
        {
            inner = $$"""{"type":"paragraph","content":[{{inner}}]}""";
        }

        var input = $$"""{"type":"doc","version":1,"content":[{{inner}}]}""";

        Assert.Throws<AdfFormatException>(() => new AdfJsonPassthrough().Convert(input));
    }

    [Fact]
    public void Over64KB_ThrowsTooLarge()
    {
        var input = Valid.Replace("hi", new string('a', AdfLimits.MaxInputBytes), StringComparison.Ordinal);

        Assert.Throws<AdfInputTooLargeException>(() => new AdfJsonPassthrough().Convert(input));
    }

    [Fact]
    public void InlineExtension_UnwrappedWithWarning_NotFailure()
    {
        const string input = """{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"a "},{"type":"inlineExtension","attrs":{"extensionType":"x","extensionKey":"y","text":"ext"}}]}]}""";

        var result = new AdfJsonPassthrough().Convert(input);

        AdfSchemaAssert.Valid(result.Document);
        Assert.DoesNotContain("inlineExtension", result.Document!.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains(result.Warnings, w => w.Contains("inlineExtension", StringComparison.Ordinal));
    }

    [Fact]
    public void JavascriptLink_Removed()
    {
        const string input = """{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"javascript:alert(1)"}}]}]}]}""";

        var result = new AdfJsonPassthrough().Convert(input);

        Assert.DoesNotContain("javascript", result.Document!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Blank_GivesNoDocument(string? input)
    {
        Assert.Null(new AdfJsonPassthrough().Convert(input).Document);
    }

    [Theory]
    [InlineData("""{"type":"doc","version":1,"content":[{"type":"text","text":"a","EVIL-KEY":1,"EVIL-KEY":2}]}""")]
    [InlineData("""{"type":"doc","version":1,"type":"doc","content":[]}""")]
    [InlineData("""{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"a","marks":[{"type":"link","attrs":{"href":"https://a.example","EVIL-KEY":1,"EVIL-KEY":2}}]}]}]}""")]
    public void DuplicateKeys_AtAnyDepth_FormatErrorWithoutEcho(string input)
    {
        var ex = Assert.Throws<AdfFormatException>(() => new AdfJsonPassthrough().Convert(input));

        Assert.DoesNotContain("EVIL-KEY", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorMessages_DoNotEchoInput()
    {
        var ex = Assert.Throws<AdfFormatException>(() => new AdfJsonPassthrough().Convert("SECRET-ISH {"));

        Assert.DoesNotContain("SECRET-ISH", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_GivesAllThree()
    {
        var factory = TestHost.Compose().GetRequiredService<IAdfConverterFactory>();

        Assert.IsType<PlainTextAdfConverter>(factory.For(JiraTextFormat.PlainText));
        Assert.IsType<MarkdownAdfConverter>(factory.For(JiraTextFormat.Markdown));
        Assert.IsType<AdfJsonPassthrough>(factory.For(JiraTextFormat.AdfJson));
        Assert.Throws<ArgumentOutOfRangeException>(() => factory.For((JiraTextFormat)99));
    }
}
