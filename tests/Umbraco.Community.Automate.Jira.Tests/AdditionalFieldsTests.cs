using System.Text.Json.Nodes;
using Umbraco.Community.Automate.Jira.Actions;

namespace Umbraco.Community.Automate.Jira.Tests;

public class AdditionalFieldsTests
{
    private static JsonObject Typed() => new() { ["summary"] = "s", ["project"] = new JsonObject { ["key"] = "SBX" } };

    [Fact]
    public void AdditionalFields_CustomFieldMerged()
    {
        var fields = Typed();
        var warnings = new List<string>();

        Assert.True(AdditionalFields.TryMerge(fields, """{"customfield_10010":5,"duedate":"2026-12-01","components":[{"name":"Web"}]}""", warnings, out var error), error);

        Assert.Equal(5, (int?)fields["customfield_10010"]);
        Assert.Equal("2026-12-01", (string?)fields["duedate"]);
        Assert.Equal("Web", (string?)fields["components"]![0]!["name"]);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AdditionalFields_Blank_NoChange(string? json)
    {
        var fields = Typed();

        Assert.True(AdditionalFields.TryMerge(fields, json, [], out _));
        Assert.True(JsonNode.DeepEquals(Typed(), fields));
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("\"x\"")]
    [InlineData("{bad")]
    [InlineData("1")]
    [InlineData("null")]
    public void AdditionalFields_NotObject_Fails(string json)
    {
        var fields = Typed();

        Assert.False(AdditionalFields.TryMerge(fields, json, [], out var error));
        Assert.NotNull(error);
        Assert.True(JsonNode.DeepEquals(Typed(), fields));
    }

    [Theory]
    [InlineData("project")]
    [InlineData("issuetype")]
    [InlineData("summary")]
    [InlineData("description")]
    [InlineData("reporter")]
    [InlineData("security")]
    [InlineData("parent")]
    public void AdditionalFields_ProtectedKey_Fails(string key)
    {
        var fields = new JsonObject();

        Assert.False(AdditionalFields.TryMerge(fields, "{\"" + key + "\":{}}", [], out var error));
        Assert.Equal($"{key} is set by its own field", error);
        Assert.Empty(fields);
    }

    [Theory]
    [InlineData("assignee")]
    [InlineData("priority")]
    [InlineData("x")]
    public void AdditionalFields_UnlistedKey_Fails(string key)
    {
        var fields = new JsonObject();

        Assert.False(AdditionalFields.TryMerge(fields, $$"""{"{{key}}":1}""", [], out var error));
        Assert.Contains(key, error);
        Assert.Empty(fields);
    }

    [Theory]
    [InlineData("""{"project":{"key":"X"}}""")]
    [InlineData("""{"Project":{}}""")]
    [InlineData("""{"PROJECT":{}}""")]
    [InlineData("""{"CUSTOMFIELD_10010":1}""")]
    [InlineData("""{"customfield_":1}""")]
    [InlineData("""{"customfield_abc":1}""")]
    [InlineData("""{"customfield_10010":1,"customfield_10010":2}""")]
    [InlineData("""{"labels":["a"],"labels":["b"]}""")]
    [InlineData("""{"customfield_1":{"x":1,"x":2}}""")]
    [InlineData("""{"project":{}}""")]
    [InlineData("""{"customfield_1":1,"customfield_1":2}""")]
    [InlineData("""{"customfield_１":1}""")]
    [InlineData("""{"customfield_1\n":1}""")]
    [InlineData("""{"customfield_1":[{"a":{"b":1,"b":2}}]}""")]
    [InlineData("""{"proj\u0065ct":{}}""")]
    [InlineData("""{"labels":[],"lab\u0065ls":[]}""")]
    [InlineData("""{"\u0050roject":{}}""")]
    public void AdditionalFields_BypassShapes_Rejected(string json)
    {
        var fields = Typed();

        Assert.False(AdditionalFields.TryMerge(fields, json, [], out var error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.True(JsonNode.DeepEquals(Typed(), fields));
    }

    [Fact]
    public void AdditionalFields_DuplicateKey_MessageNamesIt()
    {
        Assert.False(AdditionalFields.TryMerge(new JsonObject(), """{"customfield_1":{"x":1,"x":2}}""", [], out var error));
        Assert.Equal("duplicate key x", error);
    }

    [Fact]
    public void AdditionalFields_LabelsAlsoTyped_Fails()
    {
        var fields = new JsonObject { ["labels"] = new JsonArray("a") };

        Assert.False(AdditionalFields.TryMerge(fields, """{"labels":["b"]}""", [], out var error));
        Assert.Equal("set labels in one place", error);
        Assert.Equal("a", (string?)fields["labels"]![0]);
    }

    [Fact]
    public void AdditionalFields_TooDeep_Fails()
    {
        var json = "{\"customfield_1\":" + new string('[', 40) + new string(']', 40) + "}";

        Assert.False(AdditionalFields.TryMerge(new JsonObject(), json, [], out _));
    }

    [Fact]
    public void AdditionalFields_AdfValuedCustomField_IsSanitised()
    {
        var fields = new JsonObject();
        var warnings = new List<string>();
        const string json = """{"customfield_10020":{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"inlineCard","attrs":{"url":"https://x.example"}}]}]}}""";

        Assert.True(AdditionalFields.TryMerge(fields, json, warnings, out var error), error);

        Assert.DoesNotContain("inlineCard", fields.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("https://x.example", fields.ToJsonString(), StringComparison.Ordinal);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void AdditionalFields_NestedAdf_Sanitised()
    {
        const string doc = """{"type":"doc","version":1,"content":[{"type":"paragraph","content":[{"type":"text","text":"x","marks":[{"type":"link","attrs":{"href":"javascript:alert(1)"}}]}]},{"type":"extension","attrs":{"extensionKey":"k","extensionType":"t"}}]}""";
        var fields = new JsonObject();
        var warnings = new List<string>();

        Assert.True(AdditionalFields.TryMerge(fields, "{\"customfield_1\":[" + doc + "],\"customfield_2\":{\"value\":" + doc + "}}", warnings, out var error), error);

        var json = fields.ToJsonString();
        Assert.DoesNotContain("javascript", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("extension", json, StringComparison.Ordinal);
        Assert.Equal("x", (string?)fields["customfield_1"]![0]!["content"]![0]!["content"]![0]!["text"]);
        Assert.Equal("x", (string?)fields["customfield_2"]!["value"]!["content"]![0]!["content"]![0]!["text"]);
        Assert.Contains(warnings, w => w.Contains("link", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("extension", StringComparison.Ordinal));
    }

    [Fact]
    public void AdditionalFields_ErrorDoesNotEchoValues()
    {
        Assert.False(AdditionalFields.TryMerge(new JsonObject(), """{"assignee":"SECRET-VALUE"}""", [], out var error));
        Assert.DoesNotContain("SECRET-VALUE", error, StringComparison.Ordinal);
    }
}
