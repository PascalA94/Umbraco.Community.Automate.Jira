// Spike-only (Task 0 step 7, contract §5.2): builds Automate's real ConfigurationReferenceResolver and
// built-in HTTP Request action in a plain process, by reflection on the internal types, and checks
// the isolation outcomes I01-I05 against a capturing HTTP handler. Exit code 0 only if all hold.
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Actions.BuiltIn;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Automate.Core.Settings;

const string PkgKey = "Umbraco:Automate:Jira:Credentials:acme:ApiToken";
const string CtlKey = "Umbraco:Automate:Secrets:Control";
var pkg = "PKG-SENTINEL-" + Guid.NewGuid();
var ctl = "CONTROL-SENTINEL-" + Guid.NewGuid();
var failures = new List<string>();
var core = typeof(ActionInfrastructure).Assembly;
Console.WriteLine($"Umbraco.Automate.Core {core.GetName().Version}");

HttpRequestAction Build(IList<string>? allow, List<HttpRequestMessage> captured, Dictionary<HttpRequestMessage, string> headers)
{
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [PkgKey] = pkg, [CtlKey] = ctl }).Build();
    var options = new AutomateOptions();
    if (allow is not null)
    {
        options.AllowedConfigurationKeyPrefixes = allow;
    }

    var resolver = (IConfigurationReferenceResolver)Activator.CreateInstance(
        core.GetType("Umbraco.Automate.Core.Settings.ConfigurationReferenceResolver", throwOnError: true)!, config, Options.Create(options))!;
    var models = (IEditableModelResolver)Activator.CreateInstance(
        core.GetType("Umbraco.Automate.Core.Settings.EditableModelResolver", throwOnError: true)!, resolver)!;
    var services = new ServiceCollection();
    services.AddHttpClient("UmbracoAutomate").ConfigurePrimaryHttpMessageHandler(() => new Capture(captured, headers));
    var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    return new HttpRequestAction(new ActionInfrastructure(models), factory, Options.Create(new ExecutionOptions()));
}

async Task<(string? error, string wire)> Run(IList<string>? allow, string url, string? header)
{
    var captured = new List<HttpRequestMessage>();
    var headers = new Dictionary<HttpRequestMessage, string>();
    var action = Build(allow, captured, headers);
    var raw = new Dictionary<string, object?> { ["url"] = url, ["method"] = "GET" };
    if (header is not null)
    {
        raw["headers"] = new List<Dictionary<string, object?>> { new() { ["key"] = "X-Test", ["value"] = header } };
    }

    try
    {
        var settings = action.ResolveSettings(raw);
        var ctx = new ActionContext { AutomationId = Guid.NewGuid(), RunId = Guid.NewGuid(), StepId = Guid.NewGuid(), ActionAlias = action.Alias, Settings = settings, Action = action };
        await action.ExecuteAsync(ctx, CancellationToken.None);
    }
    catch (Exception ex)
    {
        return (ex.GetType().Name + ": " + ex.Message, string.Join("\n", captured.Select(r => r.RequestUri + " " + headers[r])));
    }

    return (null, string.Join("\n", captured.Select(r => r.RequestUri + " " + headers[r])));
}

void Check(string id, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {id}: {detail.Replace(pkg, "<PKG>").Replace(ctl, "<CTL>")}");
    if (!ok) failures.Add(id);
}

// I01: whole-value package reference, sensitive (header) and non-sensitive (URL) field -> not permitted.
var (e1, w1) = await Run(null, "https://x.example/a", "$" + PkgKey);
Check("I01 header whole", e1 is not null && e1.Contains("not permitted") && !e1.Contains(pkg) && w1 == "", e1 ?? "no error");
var (e1b, w1b) = await Run(null, "$" + PkgKey, null);
Check("I01 url whole", e1b is not null && e1b.Contains("not permitted") && !e1b.Contains(pkg) && w1b == "", e1b ?? "no error");
// I02/I03: embedded package reference stays literal and the value is never sent.
var (e2, w2) = await Run(null, "https://x.example/q?t=$" + PkgKey, "Bearer $" + PkgKey);
Check("I02/I03 embedded", e2 is null && w2.Contains("$" + PkgKey) && !w2.Contains(pkg), w2);
// I04 control: a Secrets reference in a sensitive field reaches the handler.
var (e4, w4) = await Run(null, "https://x.example/c", "$" + CtlKey);
Check("I04 control leaks", e4 is null && w4.Contains(ctl), w4);
// I05 control: widening the allow-list to Umbraco:Automate leaks the package value.
var widened = new List<string> { "Umbraco:Automate:Secrets", "Umbraco:Automate:Variables", "Umbraco:Automate" };
var (e5, w5) = await Run(widened, "https://x.example/q?t=$" + PkgKey, null);
Check("I05 widened leaks", e5 is null && w5.Contains(pkg), w5);
// I06: defaults of the referenced Automate version.
Check("I06 defaults", string.Join(",", new AutomateOptions().AllowedConfigurationKeyPrefixes) == "Umbraco:Automate:Secrets,Umbraco:Automate:Variables",
    string.Join(",", new AutomateOptions().AllowedConfigurationKeyPrefixes));

Console.WriteLine(failures.Count == 0 ? "ALL PASS" : "FAILED: " + string.Join(", ", failures));
return failures.Count == 0 ? 0 : 1;

sealed class Capture(List<HttpRequestMessage> captured, Dictionary<HttpRequestMessage, string> headers) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        captured.Add(request);
        headers[request] = string.Join(";", request.Headers.Where(h => h.Key == "X-Test").Select(h => h.Key + "=" + string.Join(",", h.Value)));
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("ok") });
    }
}
