using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Automate.Core.Automations;
using Umbraco.Automate.Core.Automations.Transfer;
using Umbraco.Automate.Core.Conditions;
using Umbraco.Automate.Core.Execution;
using Umbraco.Automate.Core.Runs;
using Umbraco.Automate.Core.Triggers;
using Umbraco.Automate.Core.Workspaces;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.Automate.Jira.TestSite.Spikes;

/// <summary>
/// Demo-only driver for the Task 2 and 3 spikes: creates automations through Automate's services,
/// runs them and reads the stored runs back. Development environment and loopback callers only,
/// because it creates an admin-group API user. Never part of the package.
/// </summary>
[Route("demo/spike")]
public sealed class SpikeController(
    IUserService users,
    IWorkspaceService workspaces,
    IAutomationService automations,
    IAutomationExecutor executor,
    IAutomationRunService runs,
    IWebHostEnvironment environment) : Controller
{
    private const string AdminEmail = "demo-admin@example.invalid";
    private const string ApiEmail = "demo-api@example.invalid";

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    // Loopback alone is not enough: a page open in the developer's browser can send a simple cross-site POST to
    // localhost. The custom header forces a CORS preflight (this site allows none), and the Host and Origin checks
    // block DNS rebinding. Callers (curl) send: -H 'X-Demo-Spike: 1'.
    private bool Allowed()
    {
        var request = HttpContext.Request;
        var origin = request.Headers.Origin.ToString();
        return environment.IsDevelopment()
            && System.Net.IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress!)
            && request.Headers["X-Demo-Spike"] == "1"
            && request.Host.Host is "localhost" or "127.0.0.1" or "[::1]"
            && (origin.Length == 0 || origin.StartsWith("https://localhost:", StringComparison.Ordinal) || origin.StartsWith("http://localhost:", StringComparison.Ordinal));
    }

    // Scenarios are created, published and (for manual ones) executed in one call.
    [HttpPost("scenario/{name}")]
    public async Task<IActionResult> Scenario(string name, [FromQuery] string? secret, CancellationToken ct)
    {
        if (!Allowed())
        {
            return NotFound();
        }

        var admin = users.GetByEmail(AdminEmail);
        if (admin is null)
        {
            return Conflict("unattended admin user missing");
        }

        var workspace = await EnsureWorkspaceAsync(admin, ct);
        // "loop" and "previous" are reserved binding keys, so they cannot be step aliases.
        var suffix = DateTime.UtcNow.Ticks.ToString();

        switch (name)
        {
            case "webhook-plain":
            {
                // S2: built-in Webhook trigger with the built-in plain-secret authenticator.
                var authenticator = new Dictionary<string, object?> { ["alias"] = "plain-secret", ["settings"] = new Dictionary<string, object?> { ["secret"] = secret } };
                var log1 = Step("logMessage", "umbracoAutomate.logMessage", new() { ["message"] = "KEY=${ trigger.body.issue.key }" });
                var log2 = Step("logHeaders", "umbracoAutomate.logMessage", new() { ["message"] = "HEADERS=${ trigger.headers }" });
                var log3 = Step("logBody", "umbracoAutomate.logMessage", new() { ["message"] = "BODY=${ trigger.body }" });
                var created = await CreateAsync(workspace, admin, name + suffix, "umbracoAutomate.webhook",
                    new() { ["allowedMethod"] = "POST", ["authenticator"] = authenticator },
                    [log1, log2, log3], Chain(log1, log2, log3), ct);
                return Json(new { automationId = created.Id, webhookPath = $"/automate/webhook/{created.Id}" });
            }

            case "foreach":
            {
                var iterate = Step("iterate", "umbracoAutomate.forEach", new() { ["collection"] = "${ trigger.items }", ["runParallel"] = false });
                var dump = Step("dump", "demo.dumpContext", new() { ["label"] = "item ${ loop.item } index ${ loop.index }" });
                var connections = new List<StepConnection>
                {
                    new() { SourceStepId = Guid.Empty, TargetStepId = iterate.Id },
                    new() { SourceStepId = iterate.Id, SourceHandle = "body", TargetStepId = dump.Id },
                };
                var created = await CreateAsync(workspace, admin, name + suffix, "umbracoAutomate.manual", [], [iterate, dump], connections, ct);
                var runId = await executor.ExecuteAsync(created, TriggerInitiatorType.User, admin.Key.ToString(),
                    new Dictionary<string, object?> { ["items"] = new[] { "alpha", "beta", "alpha" } }, ct);
                return Json(await WaitForRunAsync(created.Id, runId, ct));
            }

            case "retry":
            {
                var fail = Step("fail", "demo.failTransient", new() { ["label"] = "x" });
                fail.ErrorBehavior = StepErrorBehavior.Retry; // RetryInterval and MaxRetries deliberately left null: the defaults are the subject.
                var created = await CreateAsync(workspace, admin, name + suffix, "umbracoAutomate.manual", [], [fail], Chain(fail), ct);
                var runId = await executor.ExecuteAsync(created, TriggerInitiatorType.User, admin.Key.ToString(), new(), ct);
                return Json(new { automationId = created.Id, runId });
            }

            case "export":
            {
                // Shape evidence only: the draft is never published or run, and the binding expressions are
                // illustrative, not validated against real content.
                var find = Step("find", "umbracoAutomate.findContent", new() { ["name"] = "${ trigger.body.issue.key }", ["matchMode"] = "Exact" });
                var read = Step("readProp", "umbracoAutomate.getContentProperty", new() { ["contentKey"] = "${ steps.find.matches[0].contentKey }", ["propertyAlias"] = "title" });
                var branch = Step("hasTitle", "umbracoAutomate.if", new()
                {
                    // Plain dictionaries with string operators, the way the back office stores them.
                    ["conditions"] = new Dictionary<string, object?>
                    {
                        ["groups"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["conditions"] = new[] { new Dictionary<string, object?> { ["leftOperand"] = "${ steps.readProp.hasValue }", ["operator"] = "Equals", ["rightOperand"] = "true" } },
                            },
                        },
                    },
                });
                var approve = Step("approve", "umbracoAutomate.requestApproval", new() { ["prompt"] = "Publish ${ trigger.body.issue.key }?", ["timeoutHours"] = 24 });
                var publish = Step("publish", "umbracoAutomate.publishContent", new() { ["contentKey"] = "${ steps.find.matches[0].contentKey }" });
                var skipped = Step("skipped", "umbracoAutomate.logMessage", new() { ["message"] = "Nothing to publish" });
                var connections = new List<StepConnection>
                {
                    new() { SourceStepId = Guid.Empty, TargetStepId = find.Id },
                    // A filtered edge: the step runs only when the condition holds.
                    new()
                    {
                        SourceStepId = find.Id,
                        TargetStepId = read.Id,
                        Filter = new ConditionSet
                        {
                            Groups = [new ConditionGroup { Conditions = [new Condition { LeftOperand = "${ steps.find.matches.length }", Operator = ConditionOperator.GreaterThan, RightOperand = "0" }] }],
                        },
                    },
                    new() { SourceStepId = read.Id, TargetStepId = branch.Id },
                    // If: outcomes "true" and "false".
                    new() { SourceStepId = branch.Id, TargetStepId = approve.Id, Outcome = "true" },
                    new() { SourceStepId = branch.Id, TargetStepId = skipped.Id, Outcome = "false" },
                    // Request Approval: outcomes "approved" and "rejected" (RequestApprovalAction.ApprovedOutcome and RejectedOutcome).
                    new() { SourceStepId = approve.Id, TargetStepId = publish.Id, Outcome = "approved" },
                    new() { SourceStepId = approve.Id, TargetStepId = skipped.Id, Outcome = "rejected" },
                };
                var created = await CreateAsync(workspace, admin, name + suffix, "umbracoAutomate.manual", [], [find, read, branch, approve, publish, skipped], connections, ct, publish: false);
                var model = await automations.ExportAutomationAsync(created.Id, null, ct);
                return Content(JsonSerializer.Serialize(model, Pretty), "application/json");
            }

            default:
                return BadRequest();
        }
    }

    [HttpGet("runs/{automationId:guid}")]
    public async Task<IActionResult> Runs(Guid automationId, CancellationToken ct)
    {
        if (!Allowed())
        {
            return NotFound();
        }

        var (items, _) = await runs.GetRunsByAutomationPagedAsync(automationId, 0, 20, ct);
        var results = new List<object>();
        foreach (var item in items)
        {
            results.Add(await Describe(item.Id, ct));
        }

        return Json(results);
    }

    // Gives the demo API user a client-credentials login so the real Management API can be called with curl
    // (the export JSON the back office downloads). The secret is chosen by the caller and never stored in the repo.
    [HttpPost("client")]
    public async Task<IActionResult> Client([FromForm] string secret, [FromServices] Umbraco.Cms.Core.Security.IBackOfficeUserClientCredentialsManager clients)
    {
        if (!Allowed())
        {
            return NotFound();
        }

        var api = users.GetByEmail(ApiEmail);
        return api is null ? Conflict("run a scenario first") : Ok((await clients.SaveAsync(api.Key, "umbraco-back-office-demo", secret)).ToString());
    }

    // Task 8 step 5: creates a community.jira.cloud connection for the env-configured "sandbox" credential, runs
    // Automate's own connection test (the back office's Validate path) and reports whether the token appears in the
    // stored settings or the SQLite files. Returns booleans and the Validate message, never a credential.
    [HttpPost("jira-connection")]
    public async Task<IActionResult> JiraConnection(
        [FromQuery] bool gateway,
        [FromQuery] string? site,
        [FromQuery] string? name,
        [FromServices] Umbraco.Automate.Core.Connections.IConnectionService connections,
        [FromServices] IConfiguration configuration,
        CancellationToken ct)
    {
        if (!Allowed())
        {
            return NotFound();
        }

        site ??= Environment.GetEnvironmentVariable("JIRA_SANDBOX_SITE")?.Trim().TrimEnd('/');
        name ??= "sandbox";
        if (string.IsNullOrEmpty(site))
        {
            return Conflict("JIRA_SANDBOX_SITE is not set");
        }

        site = site.StartsWith("https://", StringComparison.Ordinal) ? site : "https://" + site;

        // The same site rule as the package, so this demo call can never be pointed at another host.
        if (!Umbraco.Community.Automate.Jira.Connection.JiraSiteUrl.TryNormalise(site, out var siteUri, out var siteError))
        {
            return BadRequest(siteError);
        }

        site = siteUri!.GetLeftPart(UriPartial.Authority);
        string? cloudId = null;
        if (gateway)
        {
            using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
            using var doc = JsonDocument.Parse(await http.GetStringAsync(site + "/_edge/tenant_info", ct));
            cloudId = doc.RootElement.GetProperty("cloudId").GetString();
        }

        var connection = new Umbraco.Automate.Core.Connections.Connection
        {
            Alias = "jiraSandbox" + DateTime.UtcNow.Ticks,
            Name = "Jira sandbox",
            Type = "community.jira.cloud",
            Settings = new Dictionary<string, object> { ["siteUrl"] = site, ["credentialName"] = name, ["cloudId"] = cloudId ?? string.Empty },
        };
        var created = await connections.CreateConnectionAsync(connection, null, ct);
        var result = await connections.TestConnectionAsync(created.Id, ct);
        var stored = await connections.GetConnectionAsync(created.Id, ct);
        var storedJson = JsonSerializer.Serialize(stored?.Settings);

        var token = configuration["Umbraco:Automate:Jira:Credentials:sandbox:ApiToken"] ?? string.Empty;
        var email = configuration["Umbraco:Automate:Jira:Credentials:sandbox:Email"] ?? string.Empty;
        var dataDir = Path.Combine(environment.ContentRootPath, "umbraco", "Data");
        var dbHasToken = token.Length > 0 && Directory.EnumerateFiles(dataDir, "*", SearchOption.AllDirectories)
            .Any(f => System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(f)).Contains(token, StringComparison.Ordinal));

        var message = $"{result.Status}: {result.Message}";
        return Json(new
        {
            route = gateway ? "gateway" : "site",
            validate = email.Length > 0 ? message.Replace(email, "<email>", StringComparison.Ordinal) : message,
            messageHasToken = token.Length > 0 && message.Contains(token, StringComparison.Ordinal),
            storedSettings = storedJson,
            storedHasToken = token.Length > 0 && storedJson.Contains(token, StringComparison.Ordinal),
            storedHasEmail = email.Length > 0 && storedJson.Contains(email, StringComparison.Ordinal),
            dbHasToken,
        });
    }

    [HttpGet("attempts")]
    public IActionResult Attempts() => Allowed() ? Json(FailTransientAction.Attempts.Select(a => a.ToString("O"))) : NotFound();

    private async Task<object> WaitForRunAsync(Guid automationId, Guid runId, CancellationToken ct)
    {
        for (var i = 0; i < 60; i++)
        {
            var run = await runs.GetRunAsync(runId, ct);
            if (run is { Status: not (AutomationRunStatus.Pending or AutomationRunStatus.Running) })
            {
                break;
            }

            await Task.Delay(500, ct);
        }

        return await Describe(runId, ct);
    }

    private async Task<object> Describe(Guid runId, CancellationToken ct)
    {
        var run = await runs.GetRunAsync(runId, ct);
        var trigger = await runs.GetTriggerDataAsync(runId, ct);
        return new
        {
            runId,
            status = run?.Status.ToString(),
            error = run?.Error,
            triggerData = run?.TriggerData,
            triggerDataSanitised = trigger,
            steps = run?.StepRuns.OrderBy(s => s.StartedUtc).Select(s => new
            {
                s.ActionAlias,
                s.StepId,
                status = s.Status.ToString(),
                s.Error,
                category = s.ErrorCategory?.ToString(),
                s.RetryCount,
                s.IterationIndex,
                s.IterationTotal,
                s.BranchOutcome,
                s.InputData,
                s.OutputData,
                logs = s.LogEntries.Select(l => l.Message),
            }),
        };
    }

    private async Task<Workspace> EnsureWorkspaceAsync(IUser admin, CancellationToken ct)
    {
        var service = users.GetByEmail(ApiEmail);
        if (service is null)
        {
            var created = await users.CreateAsync(admin.Key, new UserCreateModel
            {
                Email = ApiEmail,
                UserName = ApiEmail,
                Name = "Demo API",
                Kind = UserKind.Api,
                UserGroupKeys = new HashSet<Guid> { Umbraco.Cms.Core.Constants.Security.AdminGroupKey },
            }, approveUser: true);
            service = created.Result.CreatedUser!;
        }

        return await workspaces.GetWorkspaceByAliasAsync("demo", ct)
            ?? await workspaces.CreateWorkspaceAsync(new Workspace { Alias = "demo", Name = "Demo", ServiceAccountKey = service.Key }, admin.Key, ct);
    }

    private async Task<Automation> CreateAsync(
        Workspace workspace,
        IUser admin,
        string alias,
        string triggerAlias,
        Dictionary<string, object?> triggerSettings,
        IList<StepConfiguration> steps,
        IList<StepConnection> connections,
        CancellationToken ct,
        bool publish = true)
    {
        var automation = await automations.CreateAutomationAsync(new Automation
        {
            Alias = alias,
            Name = alias,
            WorkspaceId = workspace.Id,
            Trigger = new TriggerConfiguration { TriggerAlias = triggerAlias, Settings = triggerSettings },
            Steps = steps,
            Connections = connections,
        }, admin.Key, ct);
        return publish ? await automations.PublishAutomationAsync(automation.Id, admin.Key, ct) : automation;
    }

    private static StepConfiguration Step(string alias, string actionAlias, Dictionary<string, object?> settings) => new()
    {
        Id = Guid.NewGuid(),
        Alias = alias,
        Name = alias,
        ActionAlias = actionAlias,
        Settings = settings,
        ErrorBehavior = StepErrorBehavior.Terminate,
    };

    private static List<StepConnection> Chain(params StepConfiguration[] steps)
    {
        var connections = new List<StepConnection> { new() { SourceStepId = Guid.Empty, TargetStepId = steps[0].Id } };
        for (var i = 0; i < steps.Length - 1; i++)
        {
            connections.Add(new StepConnection { SourceStepId = steps[i].Id, TargetStepId = steps[i + 1].Id });
        }

        return connections;
    }
}
