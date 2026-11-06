using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Tests;

public sealed class JiraCredentialSourceCheckTests : IDisposable
{
    private const string Key = "Umbraco:Automate:Jira:Credentials:acme:ApiToken";
    private const string Sentinel = "TOKEN-SENTINEL-SRC"; // gitleaks:allow

    private readonly string _dir = Directory.CreateTempSubdirectory("jira-src-").FullName;
    private readonly string _envPrefix = $"JIRATEST_{Guid.NewGuid():N}_";

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_envPrefix + Key.Replace(":", "__", StringComparison.Ordinal), null);
        Directory.Delete(_dir, recursive: true);
    }

    private string WriteJson(string fileName, string? token = Sentinel)
    {
        var body = token is null
            ? "{}"
            : $$$$$$$"""{"Umbraco":{"Automate":{"Jira":{"Credentials":{"acme":{"ApiToken":"{{{{{{{token}}}}}}}"}}}}}}""";
        File.WriteAllText(Path.Combine(_dir, fileName), body);
        return fileName;
    }

    private IConfigurationBuilder Builder() => new ConfigurationBuilder().SetBasePath(_dir);

    private static string? Find(IConfiguration config, string name = "acme", string[]? allowed = null)
    {
        var options = new AutomateOptions();
        if (allowed is not null)
        {
            options.AllowedConfigurationKeyPrefixes = [.. options.AllowedConfigurationKeyPrefixes, .. allowed];
        }

        var monitor = new Mock<IOptionsMonitor<AutomateOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(options);
        var store = new JiraCredentialStore(config, monitor.Object, Options.Create(options));
        return new JiraCredentialSourceCheck(config, store).FindCommittedFile(name, JiraCredentialPart.ApiToken);
    }

    private void SetEnv() =>
        Environment.SetEnvironmentVariable(_envPrefix + Key.Replace(":", "__", StringComparison.Ordinal), Sentinel);

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    [InlineData("AppSettings.Production.JSON")]
    public void FindCommittedFile_AppsettingsJsonWins_ReturnsFileName(string fileName)
    {
        var config = Builder().AddJsonFile(WriteJson(fileName)).Build();

        Assert.Equal(fileName, Find(config));
    }

    [Fact]
    public void FindCommittedFile_EnvironmentVariable_ReturnsNull()
    {
        SetEnv();
        var config = Builder().AddEnvironmentVariables(_envPrefix).Build();

        Assert.Null(Find(config));
    }

    [Fact]
    public void FindCommittedFile_UserSecretsFile_ReturnsNull()
    {
        // User secrets is a JSON provider too, over secrets.json outside the repo.
        var config = Builder().AddJsonFile(WriteJson("secrets.json")).Build();

        Assert.Null(Find(config));
    }

    [Fact]
    public void FindCommittedFile_EnvironmentOverridesFile_ReturnsNull()
    {
        SetEnv();
        var config = Builder().AddJsonFile(WriteJson("appsettings.json")).AddEnvironmentVariables(_envPrefix).Build();

        Assert.Null(Find(config));
    }

    [Fact]
    public void FindCommittedFile_FileOverridesEnvironment_ReturnsFileName()
    {
        SetEnv();
        var config = Builder().AddEnvironmentVariables(_envPrefix).AddJsonFile(WriteJson("appsettings.json")).Build();

        Assert.Equal("appsettings.json", Find(config));
    }

    [Fact]
    public void FindCommittedFile_FileWithoutKey_IsNotTheWinner()
    {
        SetEnv();
        var config = Builder().AddEnvironmentVariables(_envPrefix).AddJsonFile(WriteJson("appsettings.json", token: null)).Build();

        Assert.Null(Find(config));
    }

    [Fact]
    public void FindCommittedFile_InMemoryOrMissing_ReturnsNull()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [Key] = Sentinel }).Build();

        Assert.Null(Find(config));
        Assert.Null(Find(config, "other"));
    }

    [Fact]
    public void FindCommittedFile_ChainedConfiguration_LooksInside()
    {
        // WebApplicationBuilder and test hosts can wrap one configuration inside another.
        var inner = Builder().AddJsonFile(WriteJson("appsettings.json")).Build();
        var outer = new ConfigurationBuilder().AddConfiguration(inner).Build();

        Assert.Equal("appsettings.json", Find(outer));
    }

    [Fact]
    public void FindCommittedFile_GuardTripped_ReturnsNullWithoutLooking()
    {
        var config = Builder().AddJsonFile(WriteJson("appsettings.json")).Build();

        Assert.Null(Find(config, allowed: ["Umbraco:Automate"]));
    }

    [Theory]
    [InlineData("a-b")]
    [InlineData("ACME:x")]
    public void FindCommittedFile_BadName_ReturnsNull(string name)
    {
        var config = Builder().AddJsonFile(WriteJson("appsettings.json")).Build();

        Assert.Null(Find(config, name));
    }

    [Fact]
    public void FindCommittedFile_NameIsNormalisedLikeTheStore()
    {
        var config = Builder().AddJsonFile(WriteJson("appsettings.json")).Build();

        Assert.Equal("appsettings.json", Find(config, "  ACME "));
    }

    [Fact]
    public void FindCommittedFile_NeverReturnsTheValue()
    {
        var config = Builder().AddJsonFile(WriteJson("appsettings.json")).Build();

        var result = Find(config);

        Assert.DoesNotContain(Sentinel, result ?? string.Empty, StringComparison.Ordinal);
    }
}
