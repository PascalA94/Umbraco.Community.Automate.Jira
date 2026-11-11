// credential-helper-contract: 1.0.1
global using Umbraco.Community.Automate.Jira.Credentials;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Cms.Core.Notifications;
using Xunit.Abstractions;

namespace CredentialHelperContract;

// The only test file a consumer package writes (contract section 5.3). The global using supplies the
// namespace of the package's store to the verbatim suites.
public sealed class JiraCredentialStoreContractTests : CredentialStoreContractTests<JiraCredentialPart>
{
    protected override string Prefix => JiraCredentialStore.Prefix;

    protected override string Namespace => "Umbraco.Community.Automate.Jira.Credentials";

    protected override string StoreClassName => "JiraCredentialStore";

    protected override string PartEnumName => "JiraCredentialPart";

    // Contract section 4 item 11 (C31): every package type outside the store file that is handed configuration,
    // fetches it, or names a parent of the credential section, with the reason it cannot read that section.
    protected override IReadOnlyDictionary<string, string> PinnedConfigurationReaders { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Umbraco.Community.Automate.Jira.Configuration.JiraServiceCollectionExtensions"] =
            "AddJiraCore binds JiraOptions from Umbraco:Automate:Jira (a parent of the credential section); JiraOptions has no catch-all member, so the Credentials child binds to nothing.",
        ["Umbraco.Community.Automate.Jira.Configuration.JiraComposer"] =
            "Compose passes IUmbracoBuilder.Config straight to AddJiraCore and reads nothing itself.",
        ["Umbraco.Community.Automate.Jira.Credentials.JiraCredentialSourceCheck"] =
            "Validate's committed-file warning. It calls the store's TryGet first (guard and name rule), asks providers only which one holds the store-built key, and returns a file name, never a value.",
    };

    protected override IReadOnlyList<Type> ParentSectionBoundTypes { get; } = [typeof(Umbraco.Community.Automate.Jira.Configuration.JiraOptions)];

    // KeyFor outside the store file: only the committed-file check, after the store's own TryGet has passed.
    protected override IReadOnlyDictionary<string, string> PinnedKeyForCallers { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Umbraco.Community.Automate.Jira.Credentials.JiraCredentialSourceCheck"] =
            "Builds the key to find which provider holds it, only after TryGet succeeded for the same name and part; the value is discarded and only a file name is returned.",
        ["Umbraco.Community.Automate.Jira.Webhooks.JiraSignatureWebhookAuthenticator"] =
            "Names the key in the 'secret shorter than 32 characters' warning, only after TryGet succeeded for the same name; the key goes into a log message and is never used to read configuration.",
    };

    internal override ICredentialStore<JiraCredentialPart> CreateStore(IConfiguration configuration, IOptionsMonitor<AutomateOptions> options, IOptions<AutomateOptions> startupOptions) =>
        new JiraCredentialStore(configuration, options, startupOptions);

    internal override object CreateStartupCheck(ICredentialStore<JiraCredentialPart> store, ILoggerFactory loggerFactory) =>
        new JiraCredentialStoreStartupCheck((JiraCredentialStore)store, loggerFactory.CreateLogger<JiraCredentialStoreStartupCheck>());

    internal override Task RunStartupCheck(object check)
    {
        ((JiraCredentialStoreStartupCheck)check).Handle(new UmbracoApplicationStartedNotification(false));
        return Task.CompletedTask;
    }
}

public sealed class JiraCredentialStoreIsolationTests(ITestOutputHelper output) : CredentialIsolationContractTests<JiraCredentialPart>(output)
{
    protected override string Prefix => JiraCredentialStore.Prefix;

    protected override JiraCredentialPart SamplePart => JiraCredentialPart.Email;

    internal override ICredentialStore<JiraCredentialPart> CreateStore(IConfiguration configuration, IOptionsMonitor<AutomateOptions> options, IOptions<AutomateOptions> startupOptions) =>
        new JiraCredentialStore(configuration, options, startupOptions);
}
