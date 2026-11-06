using System.Security.Cryptography;
using System.Text;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.Jira.Credentials;

namespace Umbraco.Community.Automate.Jira.Spike;

public sealed class ReadCredentialSettings
{
    [Field(Label = "Credential name")]
    public string Name { get; set; } = "spike";
}

public sealed class ReadCredentialOutput
{
    public bool Ok { get; set; }
    public string? Error { get; set; }

    // First 8 hex of SHA-256 of the value: proves which value was read, never the value.
    public string? ValueSha8 { get; set; }
}

// Spike-only (Task 0 S7): reports whether the package store can read its credential.
[Action("spike.readCredential", "Spike: read credential")]
internal sealed class ReadCredentialAction(ActionInfrastructure infrastructure, JiraCredentialStore store)
    : ActionBase<ReadCredentialSettings, ReadCredentialOutput>(infrastructure)
{
    public override Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var settings = context.GetSettings<ReadCredentialSettings>();
        var ok = store.TryGet(settings.Name, JiraCredentialPart.ApiToken, out var value, out var error);
        var sha8 = ok ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value!)))[..8] : null;
        return Task.FromResult(Success(new ReadCredentialOutput { Ok = ok, Error = error, ValueSha8 = sha8 }));
    }
}
