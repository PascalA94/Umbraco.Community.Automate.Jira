using System.Text.Json.Nodes;
using Umbraco.Community.Automate.Jira.Adf;

namespace Umbraco.Community.Automate.Jira.Tests;

public class AdfToTextTests
{
    private static string Flatten(string json) => AdfToText.Flatten(JsonNode.Parse(json));

    [Fact]
    public void TwoParagraphs_NewlineSeparated()
    {
        Assert.Equal("a\nb", Flatten("""{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"a"}]},{"type":"paragraph","content":[{"type":"text","text":"b"}]}]}"""));
    }

    [Fact]
    public void ListItems_OnePerLine()
    {
        Assert.Equal(
            "one\ntwo\nthree",
            Flatten("""{"type":"doc","version":1,"content":[{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"one"}]}]},{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"two"}]},{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph","content":[{"type":"text","text":"three"}]}]}]}]}]}]}"""));
    }

    [Fact]
    public void MarksDropped_LinkBecomesItsText()
    {
        Assert.Equal(
            "bold and link",
            Flatten("""{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"bold","marks":[{"type":"strong"}]},{"type":"text","text":" and "},{"type":"text","text":"link","marks":[{"type":"link","attrs":{"href":"https://x.example"}}]}]}]}"""));
    }

    [Fact]
    public void Mention_UsesDisplayText()
    {
        Assert.Equal(
            "hi @Ann",
            Flatten("""{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"hi "},{"type":"mention","attrs":{"id":"acc-1","text":"@Ann"}}]}]}"""));
    }

    [Fact]
    public void HardBreak_IsNewline()
    {
        Assert.Equal("a\nb", Flatten("""{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"a"},{"type":"hardBreak"},{"type":"text","text":"b"}]}]}"""));
    }

    [Fact]
    public void Null_IsEmpty()
    {
        Assert.Equal(string.Empty, AdfToText.Flatten(null));
    }

    [Fact]
    public void PlainString_IsReturnedAsIs()
    {
        // Comment bodies can arrive as a plain string rather than ADF (spike Task 4, webhooks UNKNOWN).
        Assert.Equal("just text", AdfToText.Flatten(JsonValue.Create("just text")));
    }

    [Fact]
    public void DeepNesting_DoesNotCrash()
    {
        JsonNode node = new JsonObject { ["type"] = "text", ["text"] = "deep" };
        for (var i = 0; i < 5000; i++)
        {
            node = new JsonObject { ["type"] = "blockquote", ["content"] = new JsonArray(node) };
        }

        Assert.Equal("deep", AdfToText.Flatten(node));
    }
}
