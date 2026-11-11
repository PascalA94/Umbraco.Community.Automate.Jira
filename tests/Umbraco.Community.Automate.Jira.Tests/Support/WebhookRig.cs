using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;
using Umbraco.Automate.Core.Triggers.Webhooks;
using Umbraco.Community.Automate.Jira.Webhooks;

namespace Umbraco.Community.Automate.Jira.Tests.Support;

/// <summary>
/// Composes the real authenticator through <c>AddJiraCore</c> with an in-memory credential and a fake clock,
/// and builds webhook requests the way Automate's endpoint hands them over (raw body, <c>Body</c> null).
/// </summary>
internal sealed class WebhookRig
{
    public const string CredentialName = "sbxwh";
    public const string SecretKey = "Umbraco:Automate:Jira:Credentials:sbxwh:WebhookSecret";

    // Fixture-only test secret, 36 characters.
    public const string Secret = "test-only-webhook-secret-0123456789a"; // gitleaks:allow

    public static readonly Guid AutomationId = Guid.Parse("7b65ac38-92bb-441e-8cbb-3d58326f6bd8");

    // 2025-11-04T09:30:00Z, the synthetic fixture's timestamp.
    public static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeMilliseconds(1762248600000);

    public WebhookRig(string? secret = Secret, Action<IServiceCollection>? services = null, IDictionary<string, string?>? extraConfig = null)
    {
        Time = new FakeTimeProvider(Start);
        Config = new MemoryConfigurationSource { InitialData = new Dictionary<string, string?> { [SecretKey] = secret } };
        Provider = TestHost.Compose(
            c =>
            {
                c.Add(Config);
                if (extraConfig is not null)
                {
                    c.AddInMemoryCollection(extraConfig);
                }
            },
            services: s =>
            {
                s.AddSingleton<TimeProvider>(Time);
                services?.Invoke(s);
            });
        Authenticator = ActivatorUtilities.CreateInstance<JiraSignatureWebhookAuthenticator>(Provider);
    }

    public FakeTimeProvider Time { get; }

    public MemoryConfigurationSource Config { get; }

    public IServiceProvider Provider { get; }

    public IWebhookAuthenticator Authenticator { get; }

    public CapturingLoggerProvider Logs => Provider.Logs();

    public static JiraSignatureSettings Settings(string? name = CredentialName) => new() { CredentialName = name! };

    public string Body(long? timestamp = null, string extra = "") =>
        $"{{\"timestamp\":{timestamp ?? Time.GetUtcNow().ToUnixTimeMilliseconds()},\"webhookEvent\":\"jira:issue_updated\",\"issue\":{{\"key\":\"SBX-1\"}}{extra}}}";

    public static string Sign(byte[] body, string secret = Secret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    /// <summary>
    /// The context Automate's endpoint builds with RequiresBody true: it has read the body with a StreamReader
    /// (which drops a BOM and replaces invalid UTF-8, spike Task 3 step 2) and passes the text as Body.
    /// </summary>
    public static WebhookAuthenticationContext Request(byte[] body, string?[]? signatures, object? automationId = null, string? path = null, bool sendContentLength = true)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = path ?? $"/automate/webhook/{AutomationId}";
        if (automationId is not null)
        {
            http.Request.RouteValues["automationId"] = automationId;
        }

        http.Request.ContentType = "application/json";
        if (sendContentLength)
        {
            http.Request.ContentLength = body.Length;
        }

        if (signatures is not null)
        {
            http.Request.Headers["X-Hub-Signature"] = new StringValues(signatures);
        }

        using var reader = new StreamReader(new MemoryStream(body));
        return new WebhookAuthenticationContext { Request = http.Request, Body = reader.ReadToEnd() };
    }

    public static WebhookAuthenticationContext Signed(string body, string secret = Secret, object? automationId = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        return Request(bytes, [Sign(bytes, secret)], automationId ?? AutomationId.ToString());
    }

    public bool Validate(WebhookAuthenticationContext context, JiraSignatureSettings? settings = null) =>
        Authenticator.Validate(context, settings ?? Settings());

    public void RotateSecret(string secret)
    {
        // The memory provider keeps its data across Reload(), so Set then Reload is a rotation.
        var root = (IConfigurationRoot)Provider.GetRequiredService<IConfiguration>();
        foreach (var p in root.Providers.OfType<MemoryConfigurationProvider>())
        {
            p.Set(SecretKey, secret);
        }

        root.Reload();
    }
}
