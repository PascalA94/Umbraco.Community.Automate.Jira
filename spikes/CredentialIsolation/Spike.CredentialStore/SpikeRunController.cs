using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Umbraco.Automate.Core.Automations;
using Umbraco.Automate.Core.Execution;
using Umbraco.Automate.Core.Runs;
using Umbraco.Automate.Core.Triggers;
using Umbraco.Automate.Core.Workspaces;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.Automate.Jira.Spike;

// Spike-only (Task 0 S7): creates a one-step automation, runs it with the real Automate
// pipeline (reference resolver included) and returns the stored step result.
// Local development sites only; never packed.
[Route("spike")]
public sealed class SpikeRunController(
    IUserService users,
    IWorkspaceService workspaces,
    IAutomationService automations,
    IAutomationExecutor executor,
    IAutomationRunService runs,
    IWebHostEnvironment environment) : Controller
{
    // Development sites on loopback only: these endpoints create an admin-group API user.
    private bool Allowed() => environment.IsDevelopment() && System.Net.IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress!);

    private const string Pkg = "$Umbraco:Automate:Jira:Credentials:spike:ApiToken";
    private const string Ctl = "$Umbraco:Automate:Secrets:SpikeX";

    [HttpPost("run/{name}")]
    public async Task<IActionResult> Run(string name, CancellationToken ct)
    {
        if (!Allowed())
        {
            return NotFound();
        }

        var (alias, settings) = name switch
        {
            "pkg-url-embedded" => ("umbracoAutomate.httpRequest", Http($"http://localhost:5099/pkg-url?q={Pkg}", null)),
            "pkg-header-whole" => ("umbracoAutomate.httpRequest", Http("http://localhost:5099/pkg-header-whole", Pkg)),
            "pkg-header-embedded" => ("umbracoAutomate.httpRequest", Http("http://localhost:5099/pkg-header-embedded", "Bearer " + Pkg)),
            "pkg-url-whole" => ("umbracoAutomate.httpRequest", Http(Pkg, null)),
            "control-secrets-header" => ("umbracoAutomate.httpRequest", Http("http://localhost:5099/control", Ctl)),
            "control-secrets-url" => ("umbracoAutomate.httpRequest", Http($"http://localhost:5099/control-url?q={Ctl}", null)),
            "pkg-log-whole" => ("umbracoAutomate.logMessage", new Dictionary<string, object?> { ["message"] = Pkg, ["logLevel"] = "Information" }),
            "pkg-log-embedded" => ("umbracoAutomate.logMessage", new Dictionary<string, object?> { ["message"] = "x " + Pkg + " y", ["logLevel"] = "Information" }),
            "var-log-whole" => ("umbracoAutomate.logMessage", new Dictionary<string, object?> { ["message"] = "$Umbraco:Automate:Variables:T21Var", ["logLevel"] = "Information" }),
            "read-credential" => ("spike.readCredential", new Dictionary<string, object?> { ["name"] = "spike" }),
            _ => (null, null),
        };
        if (alias is null)
        {
            return BadRequest();
        }

        var admin = users.GetByEmail("spike@example.invalid")!;
        var serviceAccount = users.GetByEmail("spike-api@example.invalid");
        if (serviceAccount is null)
        {
            var created = await users.CreateAsync(admin.Key, new UserCreateModel
            {
                Email = "spike-api@example.invalid",
                UserName = "spike-api@example.invalid",
                Name = "Spike API",
                Kind = UserKind.Api,
                UserGroupKeys = new HashSet<Guid> { Umbraco.Cms.Core.Constants.Security.AdminGroupKey },
            }, approveUser: true);
            serviceAccount = created.Result.CreatedUser!;
        }

        var workspace = await workspaces.GetWorkspaceByAliasAsync("spike", ct)
            ?? await workspaces.CreateWorkspaceAsync(new Workspace { Alias = "spike", Name = "Spike", ServiceAccountKey = serviceAccount.Key }, admin.Key, ct);

        var stepId = Guid.NewGuid();
        var automation = await automations.CreateAutomationAsync(new Automation
        {
            Alias = $"spike{name.Replace("-", "")}{DateTime.UtcNow.Ticks}",
            Name = $"Spike {name}",
            WorkspaceId = workspace.Id,
            Trigger = new TriggerConfiguration { TriggerAlias = "umbracoAutomate.manual" },
            Steps =
            {
                new StepConfiguration
                {
                    Id = stepId, ActionAlias = alias, Name = name, Settings = settings!,
                    ErrorBehavior = StepErrorBehavior.Terminate,
                },
            },
        }, admin.Key, ct);
        automation = await automations.PublishAutomationAsync(automation.Id, admin.Key, ct);

        var runId = await executor.ExecuteAsync(automation, TriggerInitiatorType.User, admin.Key.ToString(), new(), ct);
        AutomationRun? run = null;
        for (var i = 0; i < 60; i++)
        {
            run = await runs.GetRunAsync(runId, ct);
            if (run is { Status: not (AutomationRunStatus.Pending or AutomationRunStatus.Running) })
            {
                break;
            }

            await Task.Delay(500, ct);
        }

        return Json(new
        {
            name,
            runStatus = run?.Status.ToString(),
            runError = run?.Error,
            steps = run?.StepRuns.Select(s => new
            {
                s.ActionAlias,
                status = s.Status.ToString(),
                s.Error,
                category = s.ErrorCategory?.ToString(),
                s.InputData,
                s.OutputData,
                logs = s.LogEntries.Select(l => l.Message),
            }),
        });
    }

    // Gives the spike API user a client-credentials login so the surface scan (S7 step 7)
    // can call the Management API. Local throwaway user and secret.
    [HttpPost("client")]
    public async Task<IActionResult> Client([FromForm] string secret, [FromForm] string? clientId, [FromServices] Umbraco.Cms.Core.Security.IBackOfficeUserClientCredentialsManager clients)
    {
        if (!Allowed())
        {
            return NotFound();
        }

        var api = users.GetByEmail("spike-api@example.invalid");
        if (api is null)
        {
            return Conflict("run a case first");
        }

        var result = await clients.SaveAsync(api.Key, clientId ?? "umbraco-back-office-spike", secret);
        return Ok($"success={result.Success} status={result.Result}");
    }

    private static Dictionary<string, object?> Http(string url, string? headerValue)
    {
        var settings = new Dictionary<string, object?> { ["url"] = url, ["method"] = "GET" };
        if (headerValue is not null)
        {
            settings["headers"] = new List<Dictionary<string, object?>> { new() { ["key"] = "X-Test", ["value"] = headerValue } };
        }

        return settings;
    }
}
