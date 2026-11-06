using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Automate.Core.Connections;
using Umbraco.Automate.Core.Settings;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public sealed class JiraConnectionTypeTests : IDisposable
{
    private const string Token = "TOKEN-SENTINEL-VAL"; // gitleaks:allow
    private const string Email = "svc-validate@x.com";
    private const string EmailKey = "Umbraco:Automate:Jira:Credentials:acme:Email";
    private const string TokenKey = "Umbraco:Automate:Jira:Credentials:acme:ApiToken";
    private const string CloudId = "6f9619ff-8b86-d011-b42d-00c04fc964ff";
    private const string MyselfJson = """{"accountId":"acc-1","displayName":"Svc Bot"}""";
    private const string NoAdminJson = """{"permissions":{"ADMINISTER":{"havePermission":false},"ADMINISTER_PROJECTS":{"havePermission":false},"SYSTEM_ADMIN":{"havePermission":false}}}""";

    private readonly string _dir = Directory.CreateTempSubdirectory("jira-val-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Rig
    {
        public Rig(Action<IConfigurationBuilder>? config = null, string[]? allowedPrefixes = null)
        {
            Fake = new FakeJiraHandler();
            Reads = new List<string>();
            var reads = Reads;
            Provider = TestHost.Compose(
                config ?? (b => b.Add(new RecordingSource(reads, new Dictionary<string, string?> { [EmailKey] = Email, [TokenKey] = Token }))),
                Fake,
                services => services.Configure<AutomateOptions>(o =>
                {
                    if (allowedPrefixes is not null)
                    {
                        o.AllowedConfigurationKeyPrefixes = [.. o.AllowedConfigurationKeyPrefixes, .. allowedPrefixes];
                    }
                }));
            Type = ActivatorUtilities.CreateInstance<JiraConnectionType>(
                Provider,
                new ConnectionTypeInfrastructure(Mock.Of<IEditableModelResolver>()));
        }

        public FakeJiraHandler Fake { get; }

        public List<string> Reads { get; }

        public IServiceProvider Provider { get; }

        public JiraConnectionType Type { get; }

        public Task<ConnectionValidationResult> Validate(string? cloudId = CloudId, string site = "https://acme.atlassian.net", string name = "acme") =>
            Type.ValidateAsync(new JiraConnectionSettings { SiteUrl = site, CredentialName = name, CloudId = cloudId }, CancellationToken.None);

        public void AssertNoSecretAnywhere(ConnectionValidationResult result)
        {
            var everything = string.Join("\n", [result.Message ?? string.Empty, .. result.Details ?? [], .. Provider.Logs().Lines]);
            Assert.DoesNotContain(Token, everything, StringComparison.Ordinal);
            Assert.DoesNotContain(Email, everything, StringComparison.Ordinal);
        }
    }

    /// <summary>An in-memory provider that records every key it is asked for, so a test can prove nothing was read.</summary>
    private sealed class RecordingSource(List<string> reads, Dictionary<string, string?> data) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(reads, data);

        private sealed class Provider(List<string> reads, Dictionary<string, string?> data) : ConfigurationProvider
        {
            public override void Load() => Data = new Dictionary<string, string?>(data, StringComparer.OrdinalIgnoreCase);

            public override bool TryGet(string key, out string? value)
            {
                lock (reads)
                {
                    reads.Add(key);
                }

                return base.TryGet(key, out value);
            }
        }
    }

    [Fact]
    public void ConnectionType_HasPermanentAlias()
    {
        var attribute = Assert.Single(typeof(JiraConnectionType).GetCustomAttributes(typeof(ConnectionTypeAttribute), false).Cast<ConnectionTypeAttribute>());
        Assert.Equal("community.jira.cloud", attribute.Alias);
        Assert.Equal("Jira Cloud (API token)", attribute.Name);
    }

    [Fact]
    public async Task Validate_Success_ShowsSiteNameRouteAndClaims()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson).EnqueueJson(HttpStatusCode.OK, NoAdminJson);

        var result = await rig.Validate();

        Assert.Equal(ConnectionValidationStatus.Success, result.Status);
        Assert.Contains("Connected to acme.atlassian.net as Svc Bot via", result.Message);
        Assert.Contains("Atlassian gateway", result.Message);
        Assert.Contains("site-wide", result.Message);
        Assert.Contains("Automate access to this workspace", result.Message);
        Assert.DoesNotContain("admin", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new Uri($"https://api.atlassian.com/ex/jira/{CloudId}/rest/api/3/myself"), rig.Fake.Requests[0].Uri);
        rig.AssertNoSecretAnywhere(result);
    }

    [Theory]
    [InlineData(CloudId, "Atlassian gateway")]
    [InlineData(null, "site route")]
    public async Task Validate_Message_ShowsRoute(string? cloudId, string route)
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson).EnqueueJson(HttpStatusCode.OK, NoAdminJson);
        if (cloudId is null)
        {
            rig.Fake.EnqueueJson(HttpStatusCode.OK, $$"""{"cloudId":"{{CloudId}}"}""");
        }

        var result = await rig.Validate(cloudId);

        Assert.Equal(ConnectionValidationStatus.Success, result.Status);
        Assert.Contains($"via the {route}", result.Message);
    }

    [Fact]
    public async Task Validate_SiteRoute_WarnsClassicToken()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson)
            .EnqueueJson(HttpStatusCode.OK, NoAdminJson)
            .EnqueueJson(HttpStatusCode.OK, $$"""{"cloudId":"{{CloudId}}"}""");

        var result = await rig.Validate(cloudId: null);

        Assert.Equal(ConnectionValidationStatus.Success, result.Status);
        Assert.Contains("classic (unscoped) token", result.Message);
        Assert.Contains(CloudId, result.Message);
        Assert.Equal(new Uri("https://acme.atlassian.net/rest/api/3/myself"), rig.Fake.Requests[0].Uri);
        Assert.Null(rig.Fake.Requests[2].Authorization);
    }

    [Fact]
    public async Task Validate_GatewayRoute_NoClassicWarning()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson).EnqueueJson(HttpStatusCode.OK, NoAdminJson);

        var result = await rig.Validate();

        Assert.DoesNotContain("classic", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, rig.Fake.Requests.Count);
    }

    [Theory]
    [InlineData("ADMINISTER")]
    [InlineData("ADMINISTER_PROJECTS")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task Validate_AdminPermissions_Warns(string held)
    {
        var rig = new Rig();
        var permissions = NoAdminJson.Replace($"\"{held}\":{{\"havePermission\":false}}", $"\"{held}\":{{\"havePermission\":true}}", StringComparison.Ordinal);
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson).EnqueueJson(HttpStatusCode.OK, permissions);

        var result = await rig.Validate();

        Assert.Equal(ConnectionValidationStatus.Success, result.Status);
        Assert.Contains("Remove admin rights", result.Message);
        var request = rig.Fake.Requests[1];
        Assert.Equal("/rest/api/3/mypermissions", request.Uri.AbsolutePath[request.Uri.AbsolutePath.IndexOf("/rest", StringComparison.Ordinal)..]);
        Assert.Contains("permissions=ADMINISTER%2CADMINISTER_PROJECTS%2CSYSTEM_ADMIN", request.Uri.Query);
    }

    [Fact]
    public async Task Validate_PermissionCheckFails_StillConnectsAndSaysSo()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson).EnqueueJson(HttpStatusCode.InternalServerError);

        var result = await rig.Validate();

        Assert.Equal(ConnectionValidationStatus.Success, result.Status);
        Assert.Contains("Couldn't check", result.Message);
    }

    [Fact]
    public async Task Validate_CredentialFromAppsettings_Warns()
    {
        File.WriteAllText(
            Path.Combine(_dir, "appsettings.json"),
            $$$$$$$"""{"Umbraco":{"Automate":{"Jira":{"Credentials":{"acme":{"Email":"{{{{{{{Email}}}}}}}","ApiToken":"{{{{{{{Token}}}}}}}"}}}}}}""");
        var rig = new Rig(b => b.SetBasePath(_dir).AddJsonFile("appsettings.json"));
        rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson).EnqueueJson(HttpStatusCode.OK, NoAdminJson);

        var result = await rig.Validate();

        Assert.Equal(ConnectionValidationStatus.Success, result.Status);
        Assert.Contains("Credential 'acme' is read from appsettings.json", result.Message);
        Assert.Contains("environment variables", result.Message);
        rig.AssertNoSecretAnywhere(result);
    }

    [Fact]
    public async Task Validate_CredentialFromEnvironment_NoFileWarning()
    {
        var prefix = $"JIRATEST_{Guid.NewGuid():N}_";
        Environment.SetEnvironmentVariable(prefix + EmailKey.Replace(":", "__", StringComparison.Ordinal), Email);
        Environment.SetEnvironmentVariable(prefix + TokenKey.Replace(":", "__", StringComparison.Ordinal), Token);
        try
        {
            var rig = new Rig(b => b.AddEnvironmentVariables(prefix));
            rig.Fake.EnqueueJson(HttpStatusCode.OK, MyselfJson).EnqueueJson(HttpStatusCode.OK, NoAdminJson);

            var result = await rig.Validate();

            Assert.Equal(ConnectionValidationStatus.Success, result.Status);
            Assert.DoesNotContain("is read from", result.Message);
            rig.AssertNoSecretAnywhere(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(prefix + EmailKey.Replace(":", "__", StringComparison.Ordinal), null);
            Environment.SetEnvironmentVariable(prefix + TokenKey.Replace(":", "__", StringComparison.Ordinal), null);
        }
    }

    [Fact]
    public async Task Validate_401_MessageWithoutToken()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.Unauthorized, """{"errorMessages":["Client must be authenticated"]}""");

        var result = await rig.Validate();

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.StartsWith("Jira rejected the credentials (401)", result.Message);
        rig.AssertNoSecretAnywhere(result);
    }

    [Fact]
    public async Task Validate_401OnGateway_HintsAtTheValidateScope()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.Unauthorized, """{"code":401,"message":"Unauthorized; scope does not match"}""");

        var result = await rig.Validate();

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Contains("read:jira-user", result.Message);
    }

    [Theory]
    [InlineData("http://acme.atlassian.net")]
    [InlineData("https://evil.com/.atlassian.net")]
    [InlineData("")]
    public async Task Validate_BadSiteUrl_NoRequestSent(string site)
    {
        var rig = new Rig();

        var result = await rig.Validate(site: site);

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Empty(rig.Fake.Requests);
        Assert.DoesNotContain(rig.Reads, k => k.StartsWith("Umbraco:Automate:Jira:Credentials", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("abc")]
    public async Task Validate_BadCloudId_NoRequestSent(string cloudId)
    {
        var rig = new Rig();

        var result = await rig.Validate(cloudId);

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Validate_BlankCloudId_FetchesTenantInfoAndAsksUser()
    {
        var rig = new Rig();
        var settings = new JiraConnectionSettings { SiteUrl = "https://acme.atlassian.net", CredentialName = "acme", CloudId = null };
        rig.Fake.EnqueueJson(HttpStatusCode.Unauthorized, """{"errorMessages":["Client must be authenticated"]}""")
            .EnqueueJson(HttpStatusCode.OK, $$"""{"cloudId":"{{CloudId}}"}""");

        var result = await rig.Type.ValidateAsync(settings, CancellationToken.None);

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Contains(CloudId, result.Message);
        Assert.Contains("paste it into Cloud ID", result.Message);
        Assert.Null(settings.CloudId);
        var tenant = rig.Fake.Requests[1];
        Assert.Equal(new Uri("https://acme.atlassian.net/_edge/tenant_info"), tenant.Uri);
        Assert.Null(tenant.Authorization);
    }

    [Fact]
    public async Task Validate_MissingCredential_FailsNamingKeyOnly()
    {
        var reads = new List<string>();
        var rig = new Rig(b => b.Add(new RecordingSource(reads, new Dictionary<string, string?> { [EmailKey] = Email })));

        var result = await rig.Validate();

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Contains(TokenKey, result.Message);
        Assert.Empty(rig.Fake.Requests);
        rig.AssertNoSecretAnywhere(result);
    }

    [Theory]
    [InlineData("Umbraco:Automate")]
    [InlineData("Umbraco:Automate:Jira:Credentials")]
    public async Task Validate_PrefixAllowListed_FailsClosed(string prefix)
    {
        var rig = new Rig(allowedPrefixes: [prefix]);

        var result = await rig.Validate();

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Contains($"'{prefix}'", result.Message);
        Assert.Empty(rig.Fake.Requests);
    }

    [Theory]
    [InlineData("a-b")]
    [InlineData("a\nb")]
    [InlineData("agéncy")]
    [InlineData("")]
    [InlineData("..")]
    public async Task Validate_BadCredentialName_FailsBeforeLookup(string name)
    {
        var rig = new Rig();

        var result = await rig.Validate(name: name);

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Equal("Credential name must be 1 to 32 letters or digits.", result.Message);
        Assert.Empty(rig.Fake.Requests);
        Assert.DoesNotContain(rig.Reads, k => k.StartsWith("Umbraco:Automate:Jira:Credentials", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_NoSettings_Fails()
    {
        var rig = new Rig();

        var result = await rig.Type.ValidateAsync(null, CancellationToken.None);

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task Validate_NetworkError_FailsWithoutExceptionText()
    {
        var rig = new Rig();
        rig.Fake.EnqueueException(new HttpRequestException(HttpRequestError.ConnectionError, "connect failed " + Token));

        var result = await rig.Validate();

        Assert.Equal(ConnectionValidationStatus.Failure, result.Status);
        Assert.Contains("Couldn't reach Jira", result.Message);
        rig.AssertNoSecretAnywhere(result);
    }
}
