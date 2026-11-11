using System.Text.Json.Nodes;

namespace Umbraco.Community.Automate.Jira.Tests;

/// <summary>
/// The appsettings schema the package build generates and ships (plan Task 18). Read from the package project's
/// output, which the build regenerates whenever the assembly changes.
/// </summary>
public class SchemaTests
{
    private static JsonObject Definitions() => Schema()["definitions"]!.AsObject();

    private static JsonObject Schema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "Umbraco.Community.Automate.Jira", "appsettings-schema.Umbraco.Community.Automate.Jira.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    [Fact]
    public void Schema_ContainsJiraOptions()
    {
        var jira = Definitions()["JiraDefinition"]!["properties"]!.AsObject();

        Assert.True(jira.ContainsKey("TimeoutSeconds"), "TimeoutSeconds missing");
        Assert.True(jira.ContainsKey("IssueTypeCacheMinutes"), "IssueTypeCacheMinutes missing");
        Assert.True(jira.ContainsKey("ReplayWindowMinutes"), "ReplayWindowMinutes missing");
        Assert.True(jira.ContainsKey("Credentials"), "Credentials missing");
    }

    [Fact]
    public void Schema_CredentialsHaveNoDefaults()
    {
        var credential = Definitions()["JiraCredentialDefinition"]!["properties"]!.AsObject();

        Assert.Equal(["ApiToken", "Email", "WebhookSecret"], credential.Select(p => p.Key).Order(StringComparer.Ordinal));
        foreach (var (name, property) in credential)
        {
            Assert.False(property!.AsObject().ContainsKey("default"), $"{name} has a default value");
        }
    }

    [Fact]
    public void Schema_RootReachesJiraAndCredentials()
    {
        var definitions = Definitions();

        static string Ref(JsonNode property) => property["oneOf"]?[0]?["$ref"]?.GetValue<string>() ?? property["$ref"]?.GetValue<string>() ?? string.Empty;

        Assert.Equal("#/definitions/UmbracoDefinition", Ref(Schema()["properties"]!["Umbraco"]!));
        Assert.Equal("#/definitions/UmbracoAutomateDefinition", Ref(definitions["UmbracoDefinition"]!["properties"]!["Automate"]!));
        Assert.Equal("#/definitions/JiraDefinition", Ref(definitions["UmbracoAutomateDefinition"]!["properties"]!["Jira"]!));
        Assert.Equal(
            "#/definitions/JiraCredentialDefinition",
            definitions["JiraDefinition"]!["properties"]!["Credentials"]!["additionalProperties"]!["$ref"]!.GetValue<string>());
    }

    /// <summary>Every option the package binds is described in the schema, so a new option cannot ship undocumented.</summary>
    [Fact]
    public void Schema_CoversEveryJiraOption()
    {
        var jira = Definitions()["JiraDefinition"]!["properties"]!.AsObject();
        var options = typeof(Umbraco.Community.Automate.Jira.Configuration.JiraOptions).GetProperties().Select(p => p.Name);

        Assert.All(options, name => Assert.True(jira.ContainsKey(name), $"{name} is not in the schema"));
    }
}
