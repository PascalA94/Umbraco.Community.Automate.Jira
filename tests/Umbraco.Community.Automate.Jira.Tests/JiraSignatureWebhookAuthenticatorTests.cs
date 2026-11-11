using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Automate.Core.Triggers.Webhooks;
using Umbraco.Community.Automate.Jira.Tests.Support;
using Umbraco.Community.Automate.Jira.Webhooks;

namespace Umbraco.Community.Automate.Jira.Tests;

public class JiraSignatureWebhookAuthenticatorTests
{
    private const string OtherSecret = "test-only-other-secret-0123456789abc"; // gitleaks:allow

    [Fact]
    public void Alias_IsFrozenValue()
    {
        Assert.Equal("community.jira.signature", new WebhookRig().Authenticator.Alias);
    }

    [Fact]
    public void Name_IsJiraWebhookSecret()
    {
        Assert.Equal("Jira webhook secret", new WebhookRig().Authenticator.Name);
    }

    // SEC16-1: the endpoint reads the body asynchronously before Validate, so Validate never blocks on the socket.
    [Fact]
    public void RequiresBody_IsTrue()
    {
        Assert.True(new WebhookRig().Authenticator.RequiresBody);
    }

    [Fact]
    public void CredentialHeaderNames_ContainsXHubSignature()
    {
        Assert.Contains("X-Hub-Signature", new WebhookRig().Authenticator.CredentialHeaderNames);
    }

    // F-15: if the package is removed, the plain-secret fallback reads a field named Secret. Ours holds a name only.
    [Fact]
    public void Settings_FieldNamedCredentialName_NoFieldNamedSecret()
    {
        var names = typeof(JiraSignatureSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToArray();

        Assert.Equal(["CredentialName"], names);
        Assert.DoesNotContain(names, n => n.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    // SEC16-7: F-15 as an automated pin, run on both lines and on the floor and latest legs. If the package is
    // removed, Automate reads our stored settings as plain-secret settings; they hold no Secret, so it must refuse.
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" ", null)]
    [InlineData(null, "")]
    public void Fallback_PlainSecretReadingOurSettings_Refuses(string? header, string? query)
    {
        IWebhookAuthenticator plain = new Umbraco.Automate.Core.Triggers.Webhooks.BuiltIn.PlainSecretWebhookAuthenticator();
        var stored = JsonSerializer.Serialize(WebhookRig.Settings(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var asPlain = JsonSerializer.Deserialize(stored, plain.SettingsType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var context = WebhookRig.Signed("{}");
        if (header is not null)
        {
            context.Request.Headers[plain.CredentialHeaderNames.First()] = header;
        }

        if (query is not null)
        {
            context.Request.QueryString = new QueryString($"?{plain.CredentialQueryParameterNames.First()}={query}");
        }

        Assert.False(plain.Validate(context, asPlain));
    }

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var rig = new WebhookRig();

        Assert.True(rig.Validate(WebhookRig.Signed(rig.Body())));
    }

    // The plan's Validate_CapturedJiraDelivery_Passes, renamed (QA16-3): this is NOT a captured delivery (spike
    // Task 4 step 5) but a self-signed one in Atlassian's shape with non-ASCII text, so it is not the non-circular
    // proof. Replace the fixture with a real capture (Pascal's tunnel to-do, before Task 21) and rename it back.
    [Fact]
    public void Validate_SyntheticJiraShapedDelivery_Passes()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "webhooks", "issue_updated.signed.json")));
        var root = fixture.RootElement;
        var rig = new WebhookRig(root.GetProperty("secret").GetString());
        rig.Time.SetUtcNow(DateTimeOffset.FromUnixTimeMilliseconds(root.GetProperty("timestamp").GetInt64()));
        var body = Encoding.UTF8.GetBytes(root.GetProperty("body").GetString()!);
        var header = root.GetProperty("headers").GetProperty("X-Hub-Signature").GetString();

        Assert.True(rig.Validate(WebhookRig.Request(body, [header], WebhookRig.AutomationId.ToString())));
    }

    [Fact]
    public void Validate_NonAsciiBody_Passes()
    {
        var rig = new WebhookRig();

        Assert.True(rig.Validate(WebhookRig.Signed(rig.Body(extra: ",\"summary\":\"Café 🚀 Ünïcödé\""))));
    }

    // SEC16-1 trade-off: Automate's StreamReader drops a BOM, so the decoded text no longer has the signed length.
    // Such a body fails closed (Jira sends no BOM, spike Task 3 step 2) rather than being re-encoded and guessed at.
    [Fact]
    public void Validate_BomBody_FailsClosed()
    {
        var rig = new WebhookRig();
        byte[] body = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(rig.Body())];

        Assert.False(rig.Validate(WebhookRig.Request(body, [WebhookRig.Sign(body)], WebhookRig.AutomationId.ToString())));
    }

    // Invalid UTF-8 decodes to U+FFFD (three bytes each), so the decoded body is longer than Content-Length.
    [Fact]
    public void Validate_DecodedLengthDiffersFromContentLength_Fails()
    {
        var rig = new WebhookRig();
        var text = Encoding.UTF8.GetBytes(rig.Body(extra: ",\"x\":\"\""));
        byte[] body = [.. text[..^2], 0xFF, 0xFE, .. text[^2..]];
        var decoded = Encoding.UTF8.GetBytes(new StreamReader(new MemoryStream(body)).ReadToEnd());
        var context = WebhookRig.Request(body, [WebhookRig.Sign(decoded)], WebhookRig.AutomationId.ToString());

        // The signature is valid for the decoded text, so only the length check can reject it.
        Assert.False(rig.Validate(context));
    }

    // A chunked request has no Content-Length; the HMAC alone decides, over the UTF-8 of the decoded body.
    [Fact]
    public void Validate_NoContentLength_StillVerifies()
    {
        var rig = new WebhookRig();
        var body = Encoding.UTF8.GetBytes(rig.Body(extra: ",\"summary\":\"Café 🚀\""));

        Assert.True(rig.Validate(WebhookRig.Request(body, [WebhookRig.Sign(body)], WebhookRig.AutomationId.ToString(), sendContentLength: false)));
    }

    [Fact]
    public void Validate_MissingHeader_Fails()
    {
        var rig = new WebhookRig();
        var body = Encoding.UTF8.GetBytes(rig.Body());

        Assert.False(rig.Validate(WebhookRig.Request(body, null, WebhookRig.AutomationId.ToString())));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sha1=0123456789abcdef0123456789abcdef01234567")]
    [InlineData("sha256=zz")]
    [InlineData("sha256=abc")]
    public void Validate_MalformedHeader_Fails(string header)
    {
        var rig = new WebhookRig();
        var body = Encoding.UTF8.GetBytes(rig.Body());

        Assert.False(rig.Validate(WebhookRig.Request(body, [header], WebhookRig.AutomationId.ToString())));
    }

    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    public void Validate_SignatureNot64Hex_Fails(int length)
    {
        var rig = new WebhookRig();
        var body = Encoding.UTF8.GetBytes(rig.Body());
        var good = WebhookRig.Sign(body);
        var header = length < 64 ? good[..^1] : good + "0";

        Assert.False(rig.Validate(WebhookRig.Request(body, [header], WebhookRig.AutomationId.ToString())));
    }

    [Fact]
    public void Validate_MultipleSignatureHeaders_Fails()
    {
        var rig = new WebhookRig();
        var body = Encoding.UTF8.GetBytes(rig.Body());
        var good = WebhookRig.Sign(body);

        Assert.False(rig.Validate(WebhookRig.Request(body, [good, good], WebhookRig.AutomationId.ToString())));
    }

    // Council test 9: a valid shape signed with another secret.
    [Fact]
    public void Validate_ForgedSignature_Fails()
    {
        var rig = new WebhookRig();

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body(), OtherSecret)));
    }

    [Fact]
    public void Validate_WrongSecret_Fails()
    {
        var rig = new WebhookRig(OtherSecret);

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body())));
    }

    [Fact]
    public void Validate_EmptyBody_Fails()
    {
        var rig = new WebhookRig();
        var body = Array.Empty<byte>();

        Assert.False(rig.Validate(WebhookRig.Request(body, [WebhookRig.Sign(body)], WebhookRig.AutomationId.ToString())));
    }

    [Fact]
    public void Validate_NullBody_Fails()
    {
        var rig = new WebhookRig();
        var context = WebhookRig.Request([], [WebhookRig.Sign([])], WebhookRig.AutomationId.ToString());
        context = new WebhookAuthenticationContext { Request = context.Request, Body = null };

        Assert.False(rig.Validate(context));
    }

    [Fact]
    public void Validate_NullSettings_Fails()
    {
        var rig = new WebhookRig();

        Assert.False(rig.Authenticator.Validate(WebhookRig.Signed(rig.Body()), null));
    }

    [Fact]
    public void Validate_SecretShorterThan32_FailsAndWarnsWithKeyOnly()
    {
        const string shortSecret = "only-31-characters-long-secret!"; // gitleaks:allow
        Assert.Equal(31, shortSecret.Length);
        var rig = new WebhookRig(shortSecret);

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body(), shortSecret)));
        Assert.Contains(rig.Logs.Lines, l => l.Contains("Warning", StringComparison.Ordinal) && l.Contains(WebhookRig.SecretKey, StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Logs.Lines, l => l.Contains(shortSecret, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingCredential_FailsAndWarnsWithKey()
    {
        var rig = new WebhookRig(secret: null);

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body())));
        Assert.Contains(rig.Logs.Lines, l => l.Contains("Warning", StringComparison.Ordinal) && l.Contains(WebhookRig.SecretKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bad Name")]
    [InlineData("../x")]
    public void Validate_InvalidCredentialName_Fails(string name)
    {
        var rig = new WebhookRig();

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body()), WebhookRig.Settings(name)));
    }

    // Council test 2 on the inbound path: an allow-list entry that exposes the credential prefix refuses the read.
    [Theory]
    [InlineData("Umbraco:Automate")]
    [InlineData("Umbraco:Automate:Jira:Credentials")]
    public void Validate_PrefixAllowListed_Fails(string prefix)
    {
        var rig = new WebhookRig(services: s => s.Configure<AutomateOptions>(o =>
            o.AllowedConfigurationKeyPrefixes = [.. o.AllowedConfigurationKeyPrefixes, prefix]));

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body())));
        Assert.Contains(rig.Logs.Lines, l => l.Contains("Warning", StringComparison.Ordinal) && l.Contains(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TimestampOutsideWindow_Fails()
    {
        var rig = new WebhookRig();
        var now = rig.Time.GetUtcNow();
        var window = TimeSpan.FromMinutes(121);

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body((now - window).ToUnixTimeMilliseconds()))));
        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body((now + window).ToUnixTimeMilliseconds()))));
    }

    [Fact]
    public void Validate_TimestampInsideWindowEdges_Passes()
    {
        var rig = new WebhookRig();
        var now = rig.Time.GetUtcNow();
        var inside = TimeSpan.FromMinutes(119);

        Assert.True(rig.Validate(WebhookRig.Signed(rig.Body((now - inside).ToUnixTimeMilliseconds()))));
        Assert.True(rig.Validate(WebhookRig.Signed(rig.Body((now + inside).ToUnixTimeMilliseconds()))));
    }

    [Fact]
    public void Validate_ReplayWindowFromOptions()
    {
        var rig = new WebhookRig(extraConfig: new Dictionary<string, string?> { ["Umbraco:Automate:Jira:ReplayWindowMinutes"] = "30" });
        var now = rig.Time.GetUtcNow();

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body((now - TimeSpan.FromMinutes(31)).ToUnixTimeMilliseconds()))));
        Assert.True(rig.Validate(WebhookRig.Signed(rig.Body((now - TimeSpan.FromMinutes(29)).ToUnixTimeMilliseconds()))));
    }

    [Fact]
    public void Validate_TimestampMissing_Fails()
    {
        var rig = new WebhookRig();

        Assert.False(rig.Validate(WebhookRig.Signed("{\"webhookEvent\":\"jira:issue_updated\"}")));
    }

    // Plan P5: a malformed timestamp must give 401 (false), never an exception that becomes a 500.
    [Theory]
    [InlineData("\"1762248600000\"")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("1e30")]
    [InlineData("1762248600000.0")]
    [InlineData("253402300800000")]
    [InlineData("99999999999999999999999")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public void Validate_TimestampOutOfRangeOrWrongType_Returns401NotThrow(string raw)
    {
        var rig = new WebhookRig();
        var body = $"{{\"timestamp\":{raw},\"webhookEvent\":\"jira:issue_updated\"}}";

        var ex = Record.Exception(() => Assert.False(rig.Validate(WebhookRig.Signed(body))));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    public void Validate_BodyNotAnObject_Fails(string body)
    {
        var rig = new WebhookRig();

        Assert.False(rig.Validate(WebhookRig.Signed(body)));
    }

    [Fact]
    public void Validate_DuplicateTimestampKey_Fails()
    {
        var rig = new WebhookRig();
        var now = rig.Time.GetUtcNow().ToUnixTimeMilliseconds();

        Assert.False(rig.Validate(WebhookRig.Signed($"{{\"timestamp\":{now},\"timestamp\":{now},\"webhookEvent\":\"x\"}}")));
    }

    [Fact]
    public void Validate_DeeplyNestedBody_FailsWithoutThrowing()
    {
        var rig = new WebhookRig();
        var now = rig.Time.GetUtcNow().ToUnixTimeMilliseconds();
        var body = $"{{\"timestamp\":{now},\"x\":{new string('[', 40)}{new string(']', 40)}}}";

        Assert.False(rig.Validate(WebhookRig.Signed(body)));
    }

    [Fact]
    public void Validate_SameBodyTwiceWithinWindow_SecondFails()
    {
        var rig = new WebhookRig();
        var body = rig.Body();

        Assert.True(rig.Validate(WebhookRig.Signed(body)));
        rig.Time.Advance(TimeSpan.FromMinutes(75));
        Assert.False(rig.Validate(WebhookRig.Signed(body)));
    }

    // Design 6.5 step 6a: the unsigned identifier header is never part of the replay key.
    [Fact]
    public void Validate_SameBodyDifferentIdentifierHeader_SecondFails()
    {
        var rig = new WebhookRig();
        var body = rig.Body();
        var first = WebhookRig.Signed(body);
        first.Request.Headers["X-Atlassian-Webhook-Identifier"] = "id-1";
        var second = WebhookRig.Signed(body);
        second.Request.Headers["X-Atlassian-Webhook-Identifier"] = "id-2";

        Assert.True(rig.Validate(first));
        Assert.False(rig.Validate(second));
    }

    [Fact]
    public void Validate_JiraRetryHeaderPresent_FirstSeenPasses()
    {
        var rig = new WebhookRig();
        var context = WebhookRig.Signed(rig.Body());
        context.Request.Headers["X-Atlassian-Webhook-Retry"] = "1";

        Assert.True(rig.Validate(context));
    }

    // Plan test Validate_SameBodyAfterWindow_Passes, ruled: the same signed body never passes twice. Once its
    // replay entry lapses, its timestamp is outside the window too, so the scope accepts only new deliveries.
    [Fact]
    public void Validate_SameBodyAfterWindow_FailsOnTimestampAndNewBodyPasses()
    {
        var rig = new WebhookRig();
        var body = rig.Body();

        Assert.True(rig.Validate(WebhookRig.Signed(body)));

        rig.Time.Advance(TimeSpan.FromMinutes(121));
        Assert.False(rig.Validate(WebhookRig.Signed(body)));
        Assert.True(rig.Validate(WebhookRig.Signed(rig.Body())));
    }

    // A body stamped in the future stays inside the window for up to twice its length, so its replay entry must
    // live until the timestamp leaves the window, not just for one window from first sight.
    [Fact]
    public void Replay_FutureTimestamp_BlockedUntilTimestampLeavesWindow()
    {
        var rig = new WebhookRig();
        var guard = rig.Provider.GetRequiredService<WebhookReplayGuard>();
        var body = Encoding.UTF8.GetBytes(rig.Body((rig.Time.GetUtcNow() + TimeSpan.FromMinutes(119)).ToUnixTimeMilliseconds()));

        Assert.True(guard.TryAccept("scope", body, out _));

        rig.Time.Advance(TimeSpan.FromMinutes(121));
        Assert.False(guard.TryAccept("scope", body, out var reason));
        Assert.Contains("replay", reason, StringComparison.OrdinalIgnoreCase);

        rig.Time.Advance(TimeSpan.FromMinutes(119));
        Assert.False(guard.TryAccept("scope", body, out reason));
        Assert.Contains("window", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replay_ReasonNamesNoBodyContent()
    {
        var rig = new WebhookRig();
        var guard = rig.Provider.GetRequiredService<WebhookReplayGuard>();
        var body = Encoding.UTF8.GetBytes(rig.Body(extra: ",\"summary\":\"private-text\""));

        Assert.True(guard.TryAccept("scope", body, out var reason));
        Assert.Null(reason);
        Assert.False(guard.TryAccept("scope", body, out reason));
        Assert.DoesNotContain("private-text", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_KeyIsPerScope()
    {
        var rig = new WebhookRig();
        var body = rig.Body();

        Assert.True(rig.Validate(WebhookRig.Signed(body, automationId: Guid.NewGuid().ToString())));
        Assert.True(rig.Validate(WebhookRig.Signed(body, automationId: Guid.NewGuid().ToString())));
    }

    [Fact]
    public void Replay_FallbackScope_UsesPathHash()
    {
        var rig = new WebhookRig();
        var bytes = Encoding.UTF8.GetBytes(rig.Body());
        var sig = WebhookRig.Sign(bytes);

        // No automationId route value: the path decides the scope, case-insensitively.
        Assert.True(rig.Validate(WebhookRig.Request(bytes, [sig], automationId: null, path: "/automate/webhook/A")));
        Assert.False(rig.Validate(WebhookRig.Request(bytes, [sig], automationId: null, path: "/automate/webhook/a")));
        Assert.True(rig.Validate(WebhookRig.Request(bytes, [sig], automationId: null, path: "/automate/webhook/b")));
    }

    [Fact]
    public void Replay_GuidRouteValue_SameScopeAsString()
    {
        var rig = new WebhookRig();
        var body = rig.Body();

        Assert.True(rig.Validate(WebhookRig.Signed(body, automationId: WebhookRig.AutomationId)));
        Assert.False(rig.Validate(WebhookRig.Signed(body, automationId: WebhookRig.AutomationId.ToString("D").ToUpperInvariant())));
    }

    // Unauthenticated traffic must not reach the cache (design 6.5: replay check only after the HMAC passes).
    [Fact]
    public void Replay_ForgedRequestDoesNotPoisonTheCache()
    {
        var rig = new WebhookRig();
        var body = rig.Body();

        Assert.False(rig.Validate(WebhookRig.Signed(body, OtherSecret)));
        Assert.True(rig.Validate(WebhookRig.Signed(body)));
    }

    [Fact]
    public async Task Validate_ConcurrentSameBody_OnlyOneAccepted()
    {
        var rig = new WebhookRig();
        var body = rig.Body();
        using var start = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            var context = WebhookRig.Signed(body);
            start.Wait();
            return rig.Validate(context);
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public void Validate_StoreThrows_FailsClosedAndLogsTypeOnly()
    {
        const string message = "provider exploded with sensitive detail";
        var rig = new WebhookRig(services: s =>
            s.AddSingleton<IConfiguration>(new ConfigurationBuilder().Add(new ThrowingSource(message)).Build()));

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body())));
        Assert.Contains(rig.Logs.Lines, l => l.Contains("Warning", StringComparison.Ordinal) && l.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Logs.Lines, l => l.Contains(message, StringComparison.Ordinal));
    }

    // Plan P5 "the request body accessor throws": with RequiresBody true the body is already a string, so a
    // request whose headers throw stands in for any failure reading the request.
    [Fact]
    public void Validate_RequestThrows_FailsClosedAndLogsTypeOnly()
    {
        const string message = "request exploded with sensitive detail";
        var rig = new WebhookRig();
        var context = WebhookRig.Signed(rig.Body());
        var request = new Mock<HttpRequest>();
        request.SetupGet(r => r.Headers).Throws(new IOException(message));
        context = new WebhookAuthenticationContext { Request = request.Object, Body = context.Body };

        Assert.False(rig.Validate(context));
        Assert.Contains(rig.Logs.Lines, l => l.Contains("Warning", StringComparison.Ordinal) && l.Contains(nameof(IOException), StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Logs.Lines, l => l.Contains(message, StringComparison.Ordinal));
    }

    // Plan P4: the secret is read per call, so a rotation takes effect without a restart.
    [Fact]
    public void Validate_SecretRotated_UsesNewSecret()
    {
        var rig = new WebhookRig();

        Assert.True(rig.Validate(WebhookRig.Signed(rig.Body(extra: ",\"n\":1"))));

        rig.RotateSecret(OtherSecret);

        Assert.False(rig.Validate(WebhookRig.Signed(rig.Body(extra: ",\"n\":2"))));
        Assert.True(rig.Validate(WebhookRig.Signed(rig.Body(extra: ",\"n\":3"), OtherSecret)));
    }

    [Fact]
    public void Validate_NeverLogsSecretOrSignature()
    {
        var rig = new WebhookRig();
        var body = rig.Body();
        var context = WebhookRig.Signed(body);
        var signature = context.Request.Headers["X-Hub-Signature"].ToString();

        rig.Validate(context);
        rig.Validate(WebhookRig.Signed(body));
        rig.Validate(WebhookRig.Signed(body, OtherSecret));

        Assert.DoesNotContain(rig.Logs.Lines, l => l.Contains(WebhookRig.Secret, StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Logs.Lines, l => l.Contains(signature[7..], StringComparison.Ordinal));
    }

    [Fact]
    public void Composer_RegistersGuardAsSingleton()
    {
        var rig = new WebhookRig();

        Assert.Same(rig.Provider.GetRequiredService<WebhookReplayGuard>(), rig.Provider.GetRequiredService<WebhookReplayGuard>());
    }

    private sealed class ThrowingSource(string message) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(message);

        private sealed class Provider(string message) : ConfigurationProvider
        {
            public override bool TryGet(string key, out string? value) => throw new InvalidOperationException(message);
        }
    }
}
