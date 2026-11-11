using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Connections;
using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.Jira.Actions;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Tests.Support;
using Umbraco.Community.Automate.Jira.Webhooks;

namespace Umbraco.Community.Automate.Jira.Tests;

/// <summary>
/// Cross-cutting proof that the API token and webhook secret never surface (plan Task 18, design 7.6). Every case
/// composes the real services through <see cref="TestHost"/> with logging captured at Trace, puts unique sentinels
/// in the credential config, and searches every channel a value could escape through: log lines at all levels,
/// the action's message and error text, the exception chain and the serialised output.
/// </summary>
public class SecretLeakTests
{
    private static readonly string Token = "TOKEN-SENTINEL-" + Guid.NewGuid().ToString("N");
    private static readonly string WebhookSecret = "SECRET-SENTINEL-" + Guid.NewGuid().ToString("N");

    public static TheoryData<string, string> ActionsTimesResponses()
    {
        var data = new TheoryData<string, string>();
        foreach (var action in new[] { "createIssue", "addComment", "transitionIssue", "validateConnection" })
        {
            foreach (var response in new[] { "200", "400", "401", "429", "500", "302", "network", "timeout" })
            {
                data.Add(action, response);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ActionsTimesResponses))]
    public async Task NoSentinelAnywhere(string action, string response)
    {
        var rig = new ActionRig(Token, new Dictionary<string, string?>
        {
            [WebhookRig.SecretKey] = WebhookSecret,
        });

        if (response == "200")
        {
            // A realistic success, so the output, info logs and Validate's success message are searched too.
            EnqueueHappyPath(rig.Fake, action);
        }
        else
        {
            // Every request in the run gets the same answer, however many the action makes.
            for (var i = 0; i < 12; i++)
            {
                Enqueue(rig.Fake, response);
            }
        }

        var channels = new List<string>();
        try
        {
            if (action == "validateConnection")
            {
                var type = new JiraConnectionType(new ConnectionTypeInfrastructure(Mock.Of<IEditableModelResolver>()), rig.Scopes);
                var validation = await type.ValidateAsync(rig.Connection, CancellationToken.None);
                if (response == "200")
                {
                    Assert.Equal(ConnectionValidationStatus.Success, validation.Status);
                }

                channels.Add(validation.Message ?? string.Empty);
                channels.AddRange(validation.Details ?? []);
            }
            else
            {
                var result = await RunAction(rig, action);
                if (response == "200")
                {
                    Assert.Equal(ActionResultStatus.Success, result.Status);
                    Assert.NotNull(result.OutputData);
                }

                channels.Add(result.Exception?.ToString() ?? string.Empty);
                channels.Add(result.OutputData is null ? string.Empty : JsonSerializer.Serialize(result.OutputData, result.OutputData.GetType()));
                channels.AddRange(rig.LogMessages);
            }
        }
        catch (Exception ex)
        {
            channels.Add(ex.ToString());
        }

        channels.AddRange(rig.Provider.Logs().Lines);

        // Positive control: the credential really was loaded and sent, so its absence everywhere else means something.
        var basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ActionRig.Email}:{Token}"));
        Assert.Contains(rig.Fake.Requests, r => r.Authorization == basic);
        Assert.NotEmpty(channels.Where(c => !string.IsNullOrEmpty(c)));

        AssertClean(channels);
    }

    /// <summary>The leak assertion can fail: a channel holding the token, raw or as the Basic header, is caught.</summary>
    [Fact]
    public void AssertClean_CatchesAPlantedLeak()
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ActionRig.Email}:{Token}"));

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertClean(["request failed for " + Token]));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertClean(["auth " + basic]));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertClean(["hmac key " + WebhookSecret]));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("invalid")]
    [InlineData("replayed")]
    [InlineData("unsigned")]
    [InlineData("unknownCredential")]
    public void Webhook_NoSentinelAnywhere(string kind)
    {
        var rig = new WebhookRig(WebhookSecret);
        var body = rig.Body();
        var channels = new List<string>();

        var context = kind switch
        {
            "invalid" => WebhookRig.Signed(body, secret: WebhookSecret + "x"),
            "unsigned" => WebhookRig.Request(Encoding.UTF8.GetBytes(body), signatures: null, automationId: WebhookRig.AutomationId.ToString()),
            _ => WebhookRig.Signed(body, WebhookSecret),
        };

        if (kind == "replayed")
        {
            Assert.True(rig.Validate(WebhookRig.Signed(body, WebhookSecret)));
        }

        // An unconfigured credential name takes the warning path that names a config key.
        var accepted = kind == "unknownCredential"
            ? rig.Validate(context, WebhookRig.Settings("nosuchname"))
            : rig.Validate(context);
        Assert.Equal(kind == "valid", accepted);
        if (kind == "unknownCredential")
        {
            Assert.Contains(rig.Logs.Lines, l => l.Contains("nosuchname", StringComparison.OrdinalIgnoreCase));
        }

        // What core keeps of the request once the authenticator has run: headers minus the declared credential headers.
        var kept = context.Request.Headers
            .Where(h => !rig.Authenticator.CredentialHeaderNames.Contains(h.Key, StringComparer.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}: {h.Value}");
        channels.AddRange(kept);
        channels.Add(context.Body ?? string.Empty);
        channels.Add(accepted.ToString());
        channels.AddRange(rig.Logs.Lines);
        AssertClean(channels);
    }

    /// <summary>
    /// Proves only that the authenticator declares <c>X-Hub-Signature</c> for stripping. Automate's webhook endpoint
    /// removes every declared credential header before it stores the trigger output (recorded in docs/spikes.md,
    /// Task 3 step 1), so a declared header never reaches a run record. A unit test cannot see core's stored run;
    /// the end-to-end proof is Task 16 step 5 and Task 22 step 5.
    /// </summary>
    [Fact]
    public void Webhook_SignatureHeaderDeclaredForStripping()
    {
        var rig = new WebhookRig(WebhookSecret);

        Assert.Contains("X-Hub-Signature", rig.Authenticator.CredentialHeaderNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Council test 4: the connection and authenticator settings, serialised by Automate's own
    /// <c>EditableModelSerializer</c> as they are stored in the database and exported, hold the credential name only.
    /// </summary>
    [Fact]
    public void SerialisedSettings_HoldOnlyNames()
    {
        var serializer = AutomateSerializer();
        var connection = new JiraConnectionSettings { SiteUrl = "https://acme.atlassian.net", CredentialName = "acme", CloudId = ActionRig.CloudId };
        var authenticator = new JiraSignatureSettings { CredentialName = WebhookRig.CredentialName };

        var stored = new[]
        {
            serializer.Serialize(connection, schema: null)!,
            serializer.Serialize(authenticator, schema: null)!,
        };

        // Pins the stored shape: the settings carry names only (three connection fields, one authenticator field),
        // so no credential value can be stored, exported or moved by Deploy with them.
        AssertClean(stored);

        var connectionJson = JsonNode.Parse(stored[0])!.AsObject();
        Assert.Equal("acme", connectionJson["credentialName"]?.GetValue<string>() ?? connectionJson["CredentialName"]?.GetValue<string>());
        Assert.Equal(
            ["cloudId", "credentialName", "siteUrl"],
            connectionJson.Select(p => p.Key.ToLowerInvariant() switch
            {
                "cloudid" => "cloudId",
                "credentialname" => "credentialName",
                "siteurl" => "siteUrl",
                var other => other,
            }).Order(StringComparer.Ordinal).ToArray());

        var authenticatorJson = JsonNode.Parse(stored[1])!.AsObject();
        Assert.Single(authenticatorJson);
        Assert.Contains(WebhookRig.CredentialName, stored[1], StringComparison.Ordinal);
    }

    private static IEditableModelSerializer AutomateSerializer()
    {
        // Internal in Automate; its two dependencies are only used for sensitive fields, which these settings have none of.
        var type = typeof(IEditableModelSerializer).Assembly.GetType("Umbraco.Automate.Core.Settings.EditableModelSerializer", throwOnError: true)!;
        var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var args = ctor.GetParameters().Select(p => MockOf(p.ParameterType)).ToArray();
        return (IEditableModelSerializer)ctor.Invoke(args);
    }

    private static object MockOf(Type type)
    {
        var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;
        return mock.Object;
    }

    private static async Task<ActionResult> RunAction(ActionRig rig, string action) => action switch
    {
        "createIssue" => await rig.RunAsync(
            new CreateIssueAction(rig.Infrastructure, rig.Scopes),
            new CreateIssueSettings { ProjectKey = "SBX", IssueType = "Task", Summary = "Leak check" }),
        "addComment" => await rig.RunAsync(
            new AddCommentAction(rig.Infrastructure, rig.Scopes),
            new AddCommentSettings { IssueKey = "SBX-1", Body = "Leak check" }),
        "transitionIssue" => await rig.RunAsync(
            new TransitionIssueAction(rig.Infrastructure, rig.Scopes),
            new TransitionIssueSettings { IssueKey = "SBX-1", Transition = "Done" }),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static void EnqueueHappyPath(FakeJiraHandler fake, string action)
    {
        switch (action)
        {
            case "createIssue":
                fake.EnqueueJson(HttpStatusCode.OK, """{"issueTypes":[{"id":"10002","name":"Task"}]}""")
                    .EnqueueJson(HttpStatusCode.OK, """{"issues":[]}""")
                    .EnqueueJson(HttpStatusCode.Created, """{"id":"10042","key":"SBX-17","self":"https://api.atlassian.com/x"}""");
                break;
            case "addComment":
                fake.EnqueueJson(HttpStatusCode.OK, """{"startAt":0,"maxResults":50,"total":0,"comments":[]}""")
                    .EnqueueJson(HttpStatusCode.Created, """{"id":"10500","self":"https://api.atlassian.com/x","body":{"type":"doc","version":1,"content":[]}}""");
                break;
            case "transitionIssue":
                fake.EnqueueJson(HttpStatusCode.OK, """{"id":"10001","key":"SBX-1","fields":{"status":{"name":"In Progress"}}}""")
                    .EnqueueJson(HttpStatusCode.OK, """{"transitions":[{"id":"31","name":"Close","to":{"name":"Done"}}]}""")
                    .EnqueueJson(HttpStatusCode.NoContent);
                break;
            case "validateConnection":
                fake.EnqueueJson(HttpStatusCode.OK, """{"accountId":"acc-1","displayName":"Svc Bot"}""")
                    .EnqueueJson(HttpStatusCode.OK, """{"permissions":{"ADMINISTER":{"havePermission":false},"ADMINISTER_PROJECTS":{"havePermission":false},"SYSTEM_ADMIN":{"havePermission":false}}}""");
                break;
        }
    }

    private static void Enqueue(FakeJiraHandler fake, string response)
    {
        switch (response)
        {
            case "network":
                // An inner message carrying the token proves the client never surfaces raw exception text.
                fake.EnqueueException(new HttpRequestException("connection refused " + Token, new IOException("socket " + WebhookSecret)));
                break;
            case "timeout":
                fake.EnqueueException(new TaskCanceledException("timed out " + Token, new TimeoutException("timeout " + WebhookSecret)));
                break;
            case "302":
                fake.EnqueueJson(HttpStatusCode.Found, null, ("Location", "https://evil.example/steal"));
                break;
            default:
                var status = (HttpStatusCode)int.Parse(response, System.Globalization.CultureInfo.InvariantCulture);
                fake.EnqueueJson(status, status == HttpStatusCode.OK ? "{}" : """{"errorMessages":["Something went wrong"],"errors":{}}""");
                break;
        }
    }

    private static void AssertClean(IEnumerable<string> channels)
    {
        var all = string.Join("\n", channels);
        Assert.DoesNotContain(Token, all, StringComparison.Ordinal);
        Assert.DoesNotContain(WebhookSecret, all, StringComparison.Ordinal);

        // A leaked Authorization header carries the token base64-encoded with the email, never in clear.
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ActionRig.Email}:{Token}"));
        Assert.DoesNotContain(basic, all, StringComparison.Ordinal);
        Assert.DoesNotContain("Basic ", all, StringComparison.Ordinal);
    }
}
