using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Tests.Support;

/// <summary>
/// Runs a Jira action the way Automate does: resolved settings on an <see cref="ActionContext"/>, a configured
/// connection with a stable ID, and the package's real services over a <see cref="FakeJiraHandler"/>.
/// </summary>
internal sealed class ActionRig
{
    public const string CloudId = "6f9619ff-8b86-d011-b42d-00c04fc964ff";
    public const string TokenKey = "Umbraco:Automate:Jira:Credentials:acme:ApiToken";

    public ActionRig(string token = "TOKEN-SENTINEL-ACT")
    {
        Fake = new FakeJiraHandler();
        Provider = TestHost.Compose(
            b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Umbraco:Automate:Jira:Credentials:acme:Email"] = "svc-act@x.com",
                [TokenKey] = token,
            }),
            Fake);
        Connection = new JiraConnectionSettings { SiteUrl = "https://acme.atlassian.net", CredentialName = "acme", CloudId = CloudId };
    }

    public FakeJiraHandler Fake { get; }

    public IServiceProvider Provider { get; }

    public JiraConnectionSettings Connection { get; set; }

    public Guid ConnectionId { get; } = Guid.NewGuid();

    public Guid AutomationId { get; set; } = Guid.NewGuid();

    public Guid RunId { get; set; } = Guid.NewGuid();

    public Guid StepId { get; set; } = Guid.NewGuid();

    public IReadOnlyDictionary<string, object?>? BindingData { get; set; }

    public ActionContext? LastContext { get; private set; }

    public ActionInfrastructure Infrastructure { get; } = new(Mock.Of<IEditableModelResolver>());

    public IServiceScopeFactory Scopes => Provider.GetRequiredService<IServiceScopeFactory>();

    public void RotateToken(string token)
    {
        var root = (IConfigurationRoot)Provider.GetRequiredService<IConfiguration>();
        root[TokenKey] = token;
        root.Reload();
    }

    public async Task<ActionResult> RunAsync(IAction action, object settings)
    {
        LastContext = new ActionContext
        {
            AutomationId = AutomationId,
            RunId = RunId,
            StepId = StepId,
            ActionAlias = action.Alias,
            Settings = settings,
            Action = action,
            Connection = TestConnections.Jira(Connection, ConnectionId),
            BindingData = BindingData ?? new Dictionary<string, object?>(),
        };
        return await action.ExecuteAsync(LastContext, CancellationToken.None);
    }

    public IEnumerable<string> LogMessages => LastContext?.LogEntries.Select(e => e.Message) ?? [];

    public FakeJiraHandler Enqueue(HttpStatusCode status, string? json = null, params (string, string)[] headers) => Fake.EnqueueJson(status, json, headers);
}
