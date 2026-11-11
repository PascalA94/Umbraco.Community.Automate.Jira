using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Automate.Core.Connections;
using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.Jira.Actions;
using Umbraco.Community.Automate.Jira.Configuration;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Credentials;
using Xunit.Abstractions;

namespace Umbraco.Community.Automate.Jira.ContractTests;

/// <summary>Skips unless every <c>JIRA_CONTRACT_*</c> variable the live tests need is set.</summary>
public sealed class ContractFactAttribute : FactAttribute
{
    public ContractFactAttribute()
    {
        if (LiveJira.Missing() is { } missing)
        {
            Skip = $"Live Jira contract test: set {missing} to run it.";
        }
    }
}

/// <summary>
/// The live sandbox, read from environment variables into an in-memory
/// <c>Umbraco:Automate:Jira:Credentials:contract:*</c> section, so the token goes through
/// <see cref="JiraCredentialStore"/> exactly as on a site. The token must belong to a sandbox-only service
/// account (security review F-14), never a person's account.
/// </summary>
internal sealed class LiveJira
{
    public const string CredentialName = "contract";
    private static readonly string[] Required = ["JIRA_CONTRACT_SITE", "JIRA_CONTRACT_EMAIL", "JIRA_CONTRACT_TOKEN", "JIRA_CONTRACT_PROJECT"];

    public LiveJira()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Umbraco:Automate:Jira:Credentials:{CredentialName}:Email"] = Env("JIRA_CONTRACT_EMAIL"),
                [$"Umbraco:Automate:Jira:Credentials:{CredentialName}:ApiToken"] = Env("JIRA_CONTRACT_TOKEN"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddOptions<AutomateOptions>();
        services.AddJiraCore(config);
        Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Settings = new JiraConnectionSettings
        {
            SiteUrl = Env("JIRA_CONTRACT_SITE")!,
            CredentialName = CredentialName,
            CloudId = Environment.GetEnvironmentVariable("JIRA_CONTRACT_CLOUDID"),
        };
        Project = Env("JIRA_CONTRACT_PROJECT")!;
    }

    public IServiceProvider Provider { get; }

    public JiraConnectionSettings Settings { get; }

    public string Project { get; }

    public Guid ConnectionId { get; } = Guid.NewGuid();

    public IServiceScopeFactory Scopes => Provider.GetRequiredService<IServiceScopeFactory>();

    public ActionInfrastructure Infrastructure { get; } = new(Mock.Of<IEditableModelResolver>());

    public static string? Missing()
    {
        var missing = Required.Where(n => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(n))).ToArray();
        return missing.Length == 0 ? null : string.Join(", ", missing);
    }

    public JiraTarget Target()
    {
        using var scope = Provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<JiraCredentialStore>();
        Assert.True(JiraTarget.TryCreate(Settings, store, ConnectionId, out var target, out var error), error);
        return target!;
    }

    public async Task<ActionResult> RunAsync(IAction action, object settings, Guid? runId = null)
    {
        var context = new ActionContext
        {
            AutomationId = Guid.NewGuid(),
            RunId = runId ?? Guid.NewGuid(),
            StepId = Guid.NewGuid(),
            ActionAlias = action.Alias,
            Settings = settings,
            Action = action,
            Connection = Connection(),
            BindingData = new Dictionary<string, object?>(),
        };
        return await action.ExecuteAsync(context, CancellationToken.None);
    }

    /// <summary>A test-only delete, so each run leaves the sandbox as it found it. Not part of the package.</summary>
    public async Task DeleteIssueAsync(string key)
    {
        var target = Target();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(target.ApiBase, $"rest/api/3/issue/{Uri.EscapeDataString(key)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", target.AuthorizationParameter);
        using var response = await http.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"Deleting {key} returned {(int)response.StatusCode}; delete it by hand.");
    }

    private ConfiguredConnection Connection()
    {
        var connection = new Umbraco.Automate.Core.Connections.Connection
        {
            Id = ConnectionId,
            Alias = "jira",
            Name = "Jira",
            Type = "community.jira.cloud",
        };

        // The constructor is internal; the unit tests' TestConnections does the same.
        return (ConfiguredConnection)Activator.CreateInstance(
            typeof(ConfiguredConnection),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [connection, Mock.Of<IConnectionType>(), Settings],
            culture: null)!;
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);
}

public class LiveRoundTripTests(ITestOutputHelper output)
{
    [ContractFact]
    public async Task CreateCommentTransitionDelete()
    {
        var jira = new LiveJira();
        var created = await jira.RunAsync(
            new CreateIssueAction(jira.Infrastructure, jira.Scopes),
            new CreateIssueSettings { ProjectKey = jira.Project, IssueType = "Task", Summary = $"Contract test {DateTimeOffset.UtcNow:O}", Description = "Created by the package's live contract test." });
        Assert.Equal(ActionResultStatus.Success, created.Status);
        var issue = Assert.IsType<CreateIssueOutput>(created.OutputData);

        try
        {
            Assert.False(issue.WasExisting);

            var comment = await jira.RunAsync(
                new AddCommentAction(jira.Infrastructure, jira.Scopes),
                new AddCommentSettings { IssueKey = issue.IssueKey, Body = "Contract test comment" });
            Assert.Equal(ActionResultStatus.Success, comment.Status);

            using var scope = jira.Provider.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<Client.IJiraClient>();
            var transitions = await client.GetTransitionsAsync(jira.Target(), issue.IssueKey, CancellationToken.None);
            var first = Assert.IsType<Client.JiraTransition>(transitions.FirstOrDefault());

            var moved = await jira.RunAsync(
                new TransitionIssueAction(jira.Infrastructure, jira.Scopes),
                new TransitionIssueSettings { IssueKey = issue.IssueKey, Transition = first.Id });
            Assert.Equal(ActionResultStatus.Success, moved.Status);
            Assert.Equal(first.ToStatusName.Trim(), (await client.GetIssueStatusAsync(jira.Target(), issue.IssueKey, CancellationToken.None)).Trim(), ignoreCase: true);
        }
        finally
        {
            await jira.DeleteIssueAsync(issue.IssueKey);
        }
    }

    [ContractFact]
    public async Task LiveDedupe_SecondCreateWithSameKeyFindsFirst()
    {
        var jira = new LiveJira();
        var settings = new CreateIssueSettings
        {
            ProjectKey = jira.Project,
            IssueType = "Task",
            Summary = "Contract test dedupe",
            DedupeKey = "contract-" + Guid.NewGuid().ToString("N"),
        };

        var first = await jira.RunAsync(new CreateIssueAction(jira.Infrastructure, jira.Scopes), settings);
        Assert.Equal(ActionResultStatus.Success, first.Status);
        var issue = Assert.IsType<CreateIssueOutput>(first.OutputData);

        try
        {
            // Jira search is eventually consistent. Wait until the marker is searchable, and record how long that took:
            // a retry sooner than this lag would create a duplicate (README "search-lag residual").
            using var scope = jira.Provider.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<Client.IJiraClient>();
            var marker = DedupeKey.Marker(settings.DedupeKey!, jira.ConnectionId);
            var lag = Stopwatch.StartNew();
            while (await client.FindByDedupeAsync(jira.Target(), jira.Project, marker, CancellationToken.None) is null)
            {
                Assert.True(lag.Elapsed < TimeSpan.FromSeconds(60), "The dedupe marker was not searchable within 60 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            output.WriteLine($"Dedupe search lag: {lag.Elapsed.TotalSeconds:0.0} s");

            var second = await jira.RunAsync(new CreateIssueAction(jira.Infrastructure, jira.Scopes), settings);
            Assert.Equal(ActionResultStatus.Success, second.Status);
            var again = Assert.IsType<CreateIssueOutput>(second.OutputData);
            Assert.True(again.WasExisting);
            Assert.Equal(issue.IssueKey, again.IssueKey);
        }
        finally
        {
            await jira.DeleteIssueAsync(issue.IssueKey);
        }
    }
}
