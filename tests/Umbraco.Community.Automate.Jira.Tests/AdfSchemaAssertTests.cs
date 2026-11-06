using System.Text.Json.Nodes;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

// Negative controls: every "schema-valid" assertion in the suite is only as good as this validator's ability to fail.
public class AdfSchemaAssertTests
{
    private const string Para = """{"type":"paragraph","content":[{"type":"text","text":"x"}]}""";

    [Fact]
    public void ValidDocument_Passes() => Assert.True(AdfSchemaAssert.IsValid(JsonNode.Parse($$"""{"type":"doc","version":1,"content":[{{Para}}]}""")));

    [Theory]
    [InlineData("""{"type":"doc","version":1,"content":[{"type":"bulletList","content":[{"type":"listItem","content":[{"type":"heading","attrs":{"level":1},"content":[{"type":"text","text":"h"}]}]}]}]}""")]
    [InlineData("""{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"x","marks":[{"type":"code"},{"type":"strong"}]}]}]}""")]
    [InlineData("""{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":""}]}]}""")]
    [InlineData("""{"type":"doc","content":[]}""")]
    [InlineData("""{"type":"doc","version":1,"content":[{"type":"bulletList","content":[{"type":"listItem","content":[]}]}]}""")]
    [InlineData("""{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"x","extra":1}]}]}""")]
    [InlineData("""{"type":"doc","version":1,"content":[{"type":"heading","attrs":{"level":7}}]}""")]
    [InlineData("""{"type":"doc","version":2,"content":[]}""")]
    public void InvalidDocument_Fails(string json) => Assert.False(AdfSchemaAssert.IsValid(JsonNode.Parse(json)));
}
