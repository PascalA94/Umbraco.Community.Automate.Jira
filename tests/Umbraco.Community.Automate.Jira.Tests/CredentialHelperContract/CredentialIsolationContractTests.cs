// credential-helper-contract: 1.0.2
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Actions.BuiltIn;
using Umbraco.Automate.Core.Bindings;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Automate.Core.Execution;
using Umbraco.Automate.Core.Settings;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace CredentialHelperContract;

/// <summary>
/// Isolation suite (credential-helper-contract section 5.2). It runs Automate's real reference resolver,
/// binding resolver and built-in HTTP Request action, not fakes, so it fails if a release lets a step reach
/// the package's section. Copied verbatim into every consumer package.
/// The I07 reader pin is a static IL scan: it does not cover reflection invoke, <c>dynamic</c>, <c>calli</c> or
/// readers outside Umbraco.Automate.*.
/// </summary>
public abstract class CredentialIsolationContractTests<TPart>(ITestOutputHelper output)
    where TPart : struct, Enum
{
    private const string ControlKey = "Umbraco:Automate:Secrets:Control";

    // Every place Umbraco.Automate.* reaches configuration, by signature or by a call, delegate or token in a method
    // body (section 5.2, I07), keyed on the outermost type and the access it makes, each with the reason it cannot
    // read a package's section on behalf of a step. Anything new fails until a person has reviewed it and added it
    // here, because a second reader of arbitrary keys would be a second route in. The member name is left out of the
    // key because lambda names (<M>b__6_1) change between builds; a declared member is keyed by its signature, which is stable, so a new member taking IConfiguration on a pinned type is a new pair. A pin with AppliesTo is only valid on the
    // Automate versions it names, and every pin that applies must still match a reader, so a pin cannot go stale
    // and hide the next type that takes the same name.
    private sealed record ConfigurationPin(string Type, string Access, string Reason, Func<Version, bool>? AppliesTo = null);

    private static readonly IReadOnlyList<ConfigurationPin> PinnedConfigurationConsumers =
    [
        new("Umbraco.Automate.Core.Persistence.DatabaseConnectionInfo", "calls ConfigurationExtensions.GetUmbracoConnectionString", "Static Resolve/ResolveName/ResolveFromConfiguration read ConnectionStrings:<name>, the name from Umbraco:Automate:UseNamedConnectionString in app settings; no step input."),
        new("Umbraco.Automate.Core.Persistence.DatabaseConnectionInfo", "calls DatabaseConnectionInfo.ResolveFromConfiguration", "Static Resolve/ResolveName/ResolveFromConfiguration read ConnectionStrings:<name>, the name from Umbraco:Automate:UseNamedConnectionString in app settings; no step input."),
        new("Umbraco.Automate.Core.Persistence.DatabaseConnectionInfo", "calls DatabaseConnectionInfo.ResolveName", "Static Resolve/ResolveName/ResolveFromConfiguration read ConnectionStrings:<name>, the name from Umbraco:Automate:UseNamedConnectionString in app settings; no step input."),
        new("Umbraco.Automate.Core.Persistence.DatabaseConnectionInfo", "calls IConfiguration.get_Item", "Static Resolve/ResolveName/ResolveFromConfiguration read ConnectionStrings:<name>, the name from Umbraco:Automate:UseNamedConnectionString in app settings; no step input."),
        new("Umbraco.Automate.Core.Persistence.DatabaseConnectionInfo", "declares Resolve(IConfiguration)", "Static Resolve/ResolveName/ResolveFromConfiguration read ConnectionStrings:<name>, the name from Umbraco:Automate:UseNamedConnectionString in app settings; no step input."),
        new("Umbraco.Automate.Core.Persistence.DatabaseConnectionInfo", "declares Resolve(IOptionsMonitor`1, IConfiguration)", "Static Resolve/ResolveName/ResolveFromConfiguration read ConnectionStrings:<name>, the name from Umbraco:Automate:UseNamedConnectionString in app settings; no step input."),
        new("Umbraco.Automate.Core.Persistence.DatabaseConnectionInfo", "declares ResolveFromConfiguration(String, IConfiguration)", "Static Resolve/ResolveName/ResolveFromConfiguration read ConnectionStrings:<name>, the name from Umbraco:Automate:UseNamedConnectionString in app settings; no step input."),
        new("Umbraco.Automate.Core.Persistence.DatabaseConnectionInfo", "declares ResolveName(IConfiguration)", "Static Resolve/ResolveName/ResolveFromConfiguration read ConnectionStrings:<name>, the name from Umbraco:Automate:UseNamedConnectionString in app settings; no step input."),
        new("Umbraco.Automate.Core.Persistence.Scoping.ResolvingPooledDbContextFactory`1", "calls DatabaseConnectionInfo.Resolve", "TryBuildRealPool takes IConfiguration from the service provider only to call DatabaseConnectionInfo.Resolve for the connection string; fixed key, no step input. Invisible to a signature check.", v => v.Minor >= 6),
        new("Umbraco.Automate.Core.Persistence.Scoping.ResolvingPooledDbContextFactory`1", "calls ServiceProviderServiceExtensions.GetRequiredService<IConfiguration>", "TryBuildRealPool takes IConfiguration from the service provider only to call DatabaseConnectionInfo.Resolve for the connection string; fixed key, no step input. Invisible to a signature check.", v => v.Minor >= 6),
        new("Umbraco.Automate.Core.Persistence.Scoping.UmbracoAmbientAutomateConnection", "calls DatabaseConnectionInfo.Resolve", "Reads the CMS connection string to share the ambient database connection; fixed key, no step input."),
        new("Umbraco.Automate.Core.Persistence.Scoping.UmbracoAmbientAutomateConnection", "declares ctor(Lazy`1, IOptionsMonitor`1, IConfiguration)", "Reads the CMS connection string to share the ambient database connection; fixed key, no step input."),
        new("Umbraco.Automate.Core.Persistence.Scoping.UmbracoAmbientAutomateConnection", "declares field _configuration", "Reads the CMS connection string to share the ambient database connection; fixed key, no step input."),
        new("Umbraco.Automate.Core.Settings.ConfigurationReferenceResolver", "calls IConfiguration.get_Item", "The $ reference resolver itself; its allow-list is the barrier I01 to I05 and I09 test."),
        new("Umbraco.Automate.Core.Settings.ConfigurationReferenceResolver", "declares ctor(IConfiguration, IOptions`1)", "The $ reference resolver itself; its allow-list is the barrier I01 to I05 and I09 test."),
        new("Umbraco.Automate.Core.Settings.ConfigurationReferenceResolver", "declares field _configuration", "The $ reference resolver itself; its allow-list is the barrier I01 to I05 and I09 test."),
        new("Umbraco.Automate.Extensions.AmbientDbContextFactoryExtensions", "calls DatabaseConnectionInfo.Resolve", "ResolveProviderName takes IConfiguration from the service provider only to call DatabaseConnectionInfo.Resolve for the connection string; fixed key, no step input. Invisible to a signature check."),
        new("Umbraco.Automate.Extensions.AmbientDbContextFactoryExtensions", "calls ServiceProviderServiceExtensions.GetRequiredService<IConfiguration>", "ResolveProviderName takes IConfiguration from the service provider only to call DatabaseConnectionInfo.Resolve for the connection string; fixed key, no step input. Invisible to a signature check."),
        new("Umbraco.Automate.Extensions.AutomatePooledDbContextOptions", "calls DatabaseConnectionInfo.Resolve", "Configure takes IConfiguration from the service provider only to call DatabaseConnectionInfo.Resolve for the connection string; fixed key, no step input. Invisible to a signature check.", v => v.Minor <= 5),
        new("Umbraco.Automate.Extensions.AutomatePooledDbContextOptions", "calls ServiceProviderServiceExtensions.GetRequiredService<IConfiguration>", "Configure takes IConfiguration from the service provider only to call DatabaseConnectionInfo.Resolve for the connection string; fixed key, no step input. Invisible to a signature check.", v => v.Minor <= 5),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls ConfigurationBinder.Get<ExecutionOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls IConfiguration.GetSection", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls IUmbracoBuilder.get_Config", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<AutomateOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<CircuitBreakerOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<ExecutionOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<OutboxOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<RateLimitingOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<RunCleanupPolicy>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<ScheduledTriggerOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<ScriptingOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<VersionCleanupPolicy>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<WebhookOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", "calls OptionsConfigurationServiceCollectionExtensions.Configure<WorkflowLockOptions>", "AddUmbracoAutomateCore binds Automate's own typed options from fixed Umbraco:Automate sections at startup. AutomateOptions binds from the parent of the credential section, so I07 also asserts that no bound type has a catch-all member (BoundTypeScan, as C31 does for the package's own options)."),
        new("Umbraco.Automate.Persistence.AutomateSchemaInitializer", "calls DatabaseConnectionInfo.Resolve", "Reads the CMS connection string to create Automate's tables at startup; fixed key, no step input."),
        new("Umbraco.Automate.Persistence.AutomateSchemaInitializer", "declares ctor(IConfiguration, IOptionsMonitor`1, AutomateReadinessSignal, IRuntimeState, ILogger`1)", "Reads the CMS connection string to create Automate's tables at startup; fixed key, no step input."),
        new("Umbraco.Automate.Persistence.AutomateSchemaInitializer", "declares field _configuration", "Reads the CMS connection string to create Automate's tables at startup; fixed key, no step input."),
    ];

    // Automate's own options configurers are allowed (I06 runs them); a hand-written one could rewrite the
    // allow-list after binding, so each must be reviewed and named here. None exist today.
    private static readonly IReadOnlyDictionary<string, string> PinnedAutomateOptionsConfigurers = new Dictionary<string, string>(StringComparer.Ordinal);

    protected abstract string Prefix { get; }

    protected abstract TPart SamplePart { get; }

    internal abstract ICredentialStore<TPart> CreateStore(
        IConfiguration configuration,
        IOptionsMonitor<AutomateOptions> options,
        IOptions<AutomateOptions> startupOptions);

    private static IEnumerable<TPart> Parts => Enum.GetValues<TPart>();

    private string PackageKey(TPart part) => $"{Prefix}:acme:{part}";

    // The four places a step can carry a reference into a request.
    private static IEnumerable<(string Shape, string Url, string? Header)> Shapes(string reference)
    {
        yield return ("embedded URL", $"https://x.example/q?t={reference}", null);
        yield return ("whole URL", reference, null);
        yield return ("embedded header", "https://x.example/h", $"Bearer {reference}");
        yield return ("whole header", "https://x.example/w", reference);
    }

    // urlSentinel: the package value is itself a valid absolute URL, so a whole-URL leak is sent rather than
    // failing as an invalid URI. maxResponseBytes: a cap below the handler's two-byte reply makes the action fail
    // with a message that echoes the URL, which exercises the step's error channel.
    private (AutomateResolverHost Host, string PackageSentinel, string ControlSentinel) CreateHost(
        AutomateOptions? options = null,
        bool urlSentinel = false,
        long? maxResponseBytes = null)
    {
        var package = urlSentinel ? $"https://pkg-sentinel-{Guid.NewGuid():N}.example/" : $"PKG-SENTINEL-{Guid.NewGuid()}";
        var control = $"CONTROL-SENTINEL-{Guid.NewGuid()}";
        var values = new Dictionary<string, string?> { [ControlKey] = control };
        foreach (var part in Parts)
        {
            values[PackageKey(part)] = package;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var execution = new ExecutionOptions();
        if (maxResponseBytes is { } cap)
        {
            execution.MaxHttpResponseBodyBytes = cap;
        }

        return (AutomateResolverHost.Create(configuration, options ?? new AutomateOptions(), execution), package, control);
    }

    private static void AssertNoSentinel(string sentinel, StepOutcome outcome, string label)
    {
        Assert.False(outcome.Error?.Contains(sentinel) ?? false, $"{label}: the step error carries the package value.");
        Assert.False(outcome.Settings.Contains(sentinel), $"{label}: the resolved settings carry the package value.");
        Assert.False(outcome.Sent.Any(request => request.Text.Contains(sentinel)), $"{label}: a request carries the package value.");
    }

    [Fact]
    [Trait("ContractId", "I01")]
    public async Task PackageReference_WholeValue_NotPermitted()
    {
        var (host, package, _) = CreateHost();

        foreach (var part in Parts)
        {
            foreach (var sensitive in new[] { true, false })
            {
                var exception = Assert.ThrowsAny<Exception>(() => host.Resolver.Resolve("$" + PackageKey(part), typeof(string), sensitive));

                Assert.Contains("not permitted", exception.Message);
                Assert.DoesNotContain(package, exception.ToString());
            }

            // A non-string target must be refused before conversion: on 17.5.1 and 18.5.1 the conversion error echoes
            // the value, so if Automate ever converted before checking the allow-list, this is where it would leak.
            var converted = Assert.ThrowsAny<Exception>(() => host.Resolver.Resolve("$" + PackageKey(part), typeof(int), true));
            Assert.Contains("not permitted", converted.Message);
            Assert.DoesNotContain(package, converted.ToString());

            foreach (var (shape, url, header) in Shapes("$" + PackageKey(part)).Where(s => s.Shape.StartsWith("whole", StringComparison.Ordinal)))
            {
                var outcome = await host.RunHttpStepAsync(url, header);

                Assert.True(outcome.Error?.Contains("not permitted") ?? false, $"{part} {shape}: expected the not-permitted failure, got '{outcome.Error}'.");
                Assert.Empty(outcome.Sent);
                AssertNoSentinel(package, outcome, $"{part} {shape}");
            }
        }
    }

    [Fact]
    [Trait("ContractId", "I02")]
    public async Task PackageReference_Embedded_StaysLiteral()
    {
        var (host, package, _) = CreateHost();

        foreach (var part in Parts)
        {
            var literal = "Bearer $" + PackageKey(part);

            var resolved = host.Resolver.Resolve(literal, typeof(string), false);

            Assert.Equal(literal, resolved);
            Assert.DoesNotContain(package, resolved?.ToString());

            var outcome = await host.RunHttpStepAsync("https://x.example/e", literal);
            Assert.Null(outcome.Error);
            Assert.Contains(JsonSerializer.Serialize(literal), outcome.Settings);
            AssertNoSentinel(package, outcome, $"{part} embedded header");
        }
    }

    [Fact]
    [Trait("ContractId", "I03")]
    public async Task HttpRequestStep_NeverSendsPackageValue()
    {
        var (host, package, _) = CreateHost();

        foreach (var part in Parts)
        {
            var reference = "$" + PackageKey(part);
            foreach (var (shape, url, header) in Shapes(reference))
            {
                var outcome = await host.RunHttpStepAsync(url, header);
                var label = $"{part} {shape}";
                AssertNoSentinel(package, outcome, label);

                if (shape.StartsWith("whole", StringComparison.Ordinal))
                {
                    // A whole-value reference outside the allow-list is refused before anything is sent.
                    Assert.True(outcome.Error?.Contains("not permitted") ?? false, $"{label}: expected the not-permitted failure, got '{outcome.Error}'.");
                    Assert.Empty(outcome.Sent);
                }
                else
                {
                    // An embedded one stays literal and is sent as written, which also proves the handler sees requests.
                    Assert.True(outcome.Error is null, $"{label}: {outcome.Error}");
                    var sent = Assert.Single(outcome.Sent);
                    Assert.Contains(shape == "embedded URL" ? PackageKey(part) : $"X-Test=Bearer {reference}", sent.Text);
                }
            }
        }

        // A package value that is itself a valid URL: had the whole-URL reference resolved, the request would go to
        // the value. I05 proves it does when the allow-list is widened.
        var (urlHost, urlPackage, _) = CreateHost(urlSentinel: true);
        foreach (var part in Parts)
        {
            foreach (var (shape, url, header) in Shapes("$" + PackageKey(part)))
            {
                AssertNoSentinel(urlPackage, await urlHost.RunHttpStepAsync(url, header), $"{part} {shape} (URL-shaped value)");
            }
        }

        // The error channel: a response over the cap fails the step with a message that echoes the URL. I05 proves
        // a resolved value reaches that message when the allow-list is widened.
        var (cappedHost, cappedPackage, _) = CreateHost(maxResponseBytes: 1);
        foreach (var part in Parts)
        {
            var outcome = await cappedHost.RunHttpStepAsync($"https://x.example/q?t=${PackageKey(part)}", null);
            Assert.True(outcome.Error is not null, $"{part}: the capped run did not fail, so the error channel was not exercised.");
            AssertNoSentinel(cappedPackage, outcome, $"{part} embedded URL (error channel)");
        }
    }

    [Fact]
    [Trait("ContractId", "I04")]
    public async Task Control_SecretsPrefixValueLeaks()
    {
        var (host, _, control) = CreateHost();

        var outcome = await host.RunHttpStepAsync("https://x.example/c", $"${ControlKey}");

        Assert.Null(outcome.Error);
        Assert.Contains(outcome.Sent, request => request.Text.Contains(control));
    }

    [Fact]
    [Trait("ContractId", "I05")]
    public async Task Control_WideningAllowList_LeaksPackageValue()
    {
        var widened = new AutomateOptions();
        widened.AllowedConfigurationKeyPrefixes = [.. widened.AllowedConfigurationKeyPrefixes, "Umbraco:Automate"];
        var (host, package, _) = CreateHost(widened);

        foreach (var (shape, url, header) in Shapes("$" + PackageKey(SamplePart)))
        {
            var outcome = await host.RunHttpStepAsync(url, header);
            output.WriteLine($"{shape}: error '{outcome.Error}', {outcome.Sent.Count} sent");

            // A whole URL that is only the value is not a valid request URI, so nothing is sent; the value still
            // sits in the resolved settings, where a step run would record it.
            var leaked = shape == "whole URL"
                ? outcome.Settings.Contains(package)
                : outcome.Sent.Any(request => request.Text.Contains(package));
            Assert.True(leaked, $"{shape}: the widened allow-list did not leak the package value, so the harness cannot see a leak.");
        }

        // A whole-URL reference whose value is a valid URL is sent to that value, so the request channel sees a
        // whole-URL leak too, not only the resolved settings.
        var (urlHost, urlPackage, _) = CreateHost(widened, urlSentinel: true);
        var urlOutcome = await urlHost.RunHttpStepAsync("$" + PackageKey(SamplePart), null);
        output.WriteLine($"whole URL (URL-shaped value): error '{urlOutcome.Error}', {urlOutcome.Sent.Count} sent");
        Assert.True(urlOutcome.Sent.Any(request => request.Text.Contains(urlPackage)), "whole URL: a URL-shaped package value was not sent, so the request channel cannot see a whole-URL leak.");

        // The error channel: the action echoes the URL when the response is over the cap, so a resolved value
        // reaches the step's failure text. If this fails, I03's error-text assertions prove nothing.
        var (cappedHost, cappedPackage, _) = CreateHost(widened, maxResponseBytes: 1);
        var cappedOutcome = await cappedHost.RunHttpStepAsync($"https://x.example/q?t=${PackageKey(SamplePart)}", null);
        output.WriteLine($"embedded URL over the response cap: error '{cappedOutcome.Error}'");
        Assert.True(cappedOutcome.Error?.Contains(cappedPackage) ?? false, $"error channel: the failure text does not carry the package value: '{cappedOutcome.Error}'.");

        // Core's type conversion echoes a value it cannot convert. A step's settings never reach it with a string
        // for a non-string field (the settings are deserialised before references are resolved), so this is shown
        // on the resolver directly: it is the channel a future settings shape would open.
        // Automate 17.5.1 and 18.5.1 echo the value in this error ("Cannot convert configuration value '<value>' ...");
        // 17.6.0 and 18.6.0 name only the key ("... value for '<key>' ..."). Either is accepted, but it must be the
        // conversion error: the not-permitted refusal also names the key, so the message shape is checked, not the key
        // alone or the exception type (SettingsResolutionException is new in 17.6.0 and 18.6.0 and is also the refusal's
        // type there). The run output records which behaviour this Automate has.
        var conversion = Assert.ThrowsAny<Exception>(() => host.Resolver.Resolve("$" + PackageKey(SamplePart), typeof(int), true));
        var conversionEchoes = conversion.ToString().Contains(package, StringComparison.Ordinal);
        output.WriteLine($"type conversion: {conversion.GetType().Name}, echoes the value: {conversionEchoes}");
        Assert.DoesNotContain("not permitted", conversion.Message);
        Assert.Contains("Cannot convert configuration value", conversion.Message);
        Assert.True(
            conversionEchoes || conversion.Message.Contains($"for '{PackageKey(SamplePart)}'", StringComparison.Ordinal),
            $"type conversion: the error names neither the value nor the key: {conversion.GetType().Name}");

        var store = CreateStore(host.Configuration, new TestOptionsMonitor<AutomateOptions>(widened), Options.Create(widened));
        Assert.False(store.TryGet("acme", SamplePart, out var value, out var storeError));
        Assert.Null(value);
        Assert.Equal(
            $"Refused: Umbraco:Automate:AllowedConfigurationKeyPrefixes contains 'Umbraco:Automate', which exposes {Prefix}. Remove that entry.",
            storeError);
    }

    // Non-string targets whose conversion the resolver leaves to a later step: on all four versions (17.5.1, 18.5.1,
    // 17.6.0, 18.6.0) it returns the raw string for these (only int is converted inside the resolver), so the later
    // conversion's error is the channel.
    private static readonly Type[] ConversionTargets = [typeof(float), typeof(Guid), typeof(DateTime), typeof(DayOfWeek), typeof(TimeSpan)];

    // The conversion a consumer would apply to the raw string the resolver hands back. These are the BCL parsers,
    // not Automate code, so the echo flag below describes the runtime's messages, not Automate's.
    private static void DownstreamConversion(Type target, string raw)
    {
        if (target == typeof(float))
        {
            _ = float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (target == typeof(Guid))
        {
            _ = Guid.Parse(raw);
        }
        else if (target == typeof(DateTime))
        {
            _ = DateTime.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (target == typeof(TimeSpan))
        {
            _ = TimeSpan.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            _ = Enum.Parse(target, raw);
        }
    }

    // I05 variants for the non-int targets. Widened: the resolver must hand back the raw package string unconverted
    // for every target, which is the behaviour on all four versions. A release that starts converting, or throws,
    // fails here so the README and SECURITY.md wording is re-checked. The run output also records whether a later
    // BCL conversion of that string echoes the value.
    [Fact]
    [Trait("ContractId", "I05b")]
    public void Control_WideningAllowList_NonStringTargets_ReportsConversionChannel()
    {
        var widened = new AutomateOptions();
        widened.AllowedConfigurationKeyPrefixes = [.. widened.AllowedConfigurationKeyPrefixes, "Umbraco:Automate"];
        var (host, package, _) = CreateHost(widened);
        var reference = "$" + PackageKey(SamplePart);

        foreach (var target in ConversionTargets)
        {
            object? resolved = null;
            Exception? thrown = null;
            try
            {
                resolved = host.Resolver.Resolve(reference, target, true);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.True(thrown is null, $"{target.Name}: the resolver threw {thrown?.GetType().Name} ('{thrown?.Message}') instead of returning the raw string; re-check the README and SECURITY.md wording.");

            var raw = resolved as string;
            var converted = resolved is not null && resolved.GetType() == target;
            output.WriteLine($"{target.Name}: resolver returned {resolved?.GetType().Name ?? "null"}, raw string equals the package value: {raw == package}, converted: {converted}");
            Assert.True(raw == package && !converted, $"{target.Name}: expected the raw package string unconverted, got {resolved?.GetType().Name ?? "null"}; re-check the README and SECURITY.md wording.");

            if (raw is not null)
            {
                var conversion = Assert.ThrowsAny<Exception>(() => DownstreamConversion(target, raw));
                output.WriteLine($"{target.Name}: a later BCL conversion throws {conversion.GetType().Name}, echoes the value: {conversion.ToString().Contains(package, StringComparison.Ordinal)}; message '{conversion.Message}'");
            }
        }
    }

    // The same targets with the default allow-list: the guard runs before any conversion, so no target type can
    // turn a package reference into an error that carries its value, or into a value at all.
    [Fact]
    [Trait("ContractId", "I05c")]
    public void PackageReference_NonStringTargets_RefusedBeforeConversion()
    {
        var (host, package, _) = CreateHost();

        foreach (var part in Parts)
        {
            foreach (var target in ConversionTargets)
            {
                foreach (var sensitive in new[] { true, false })
                {
                    var exception = Assert.ThrowsAny<Exception>(() => host.Resolver.Resolve("$" + PackageKey(part), target, sensitive));

                    Assert.Contains("not permitted", exception.Message);
                    Assert.DoesNotContain(package, exception.ToString());
                }
            }
        }
    }

    // Runs Automate's own registration, as a site does, so a release that changes the defaults or adds a
    // configurer that widens the allow-list is caught, not just a change to the AutomateOptions initialiser.
    [Fact]
    [Trait("ContractId", "I06")]
    public void DefaultAllowList_OfReferencedAutomate_ExcludesPrefix()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        AutomateRegistration.AddUmbracoAutomate(services, configuration);

        var configurers = services
            .Where(d => d.ServiceType == typeof(IConfigureOptions<AutomateOptions>) || d.ServiceType == typeof(IPostConfigureOptions<AutomateOptions>))
            .Select(d => (d.ImplementationType ?? d.ImplementationInstance?.GetType() ?? d.ImplementationFactory?.Method.DeclaringType)?.FullName ?? "?")
            .ToList();
        output.WriteLine("AutomateOptions configurers registered: " + string.Join(", ", configurers));

        // Positive control: Automate's registration really ran and bound AutomateOptions from configuration.
        Assert.NotEmpty(configurers);

        using var provider = services.BuildServiceProvider();
        var bound = provider.GetRequiredService<IOptions<AutomateOptions>>().Value;
        var entries = bound.AllowedConfigurationKeyPrefixes ?? [];
        output.WriteLine("Default AllowedConfigurationKeyPrefixes: " + string.Join(", ", entries.Select(e => $"'{e}'")));

        var store = CreateStore(configuration, new TestOptionsMonitor<AutomateOptions>(bound), Options.Create(bound));
        Assert.Empty(store.GetExposingPrefixes());

        // A configurer type in Automate's assemblies could be registered somewhere the call above does not reach.
        var configurerTypes = ConfigurationConsumerScan.LoadAutomateAssemblies()
            .SelectMany(ConfigurationConsumerScan.TypesOf)
            .Where(t => !t.IsAbstract && (typeof(IConfigureOptions<AutomateOptions>).IsAssignableFrom(t) || typeof(IPostConfigureOptions<AutomateOptions>).IsAssignableFrom(t)))
            .Select(t => t.FullName!)
            .Where(name => !PinnedAutomateOptionsConfigurers.ContainsKey(name))
            .ToList();
        Assert.True(configurerTypes.Count == 0, "Unpinned AutomateOptions configurers in Umbraco.Automate.*: " + string.Join(", ", configurerTypes));
    }

    // IL member-reference pin (section 5.2, I07). A signature pin misses a reader that resolves IConfiguration inside
    // a method body (GetRequiredService<IConfiguration>), so every method body in Umbraco.Automate.* is scanned for
    // calls, delegate creations and tokens that reach configuration. A (type, access) pair that only appears after an
    // Automate patch is a new route to configuration and fails until a person has reviewed it.
    [Fact]
    [Trait("ContractId", "I07")]
    public void AutomateConfigurationConsumers_ArePinned()
    {
        var assemblies = ConfigurationConsumerScan.LoadAutomateAssemblies().ToList();
        var scanned = assemblies.Select(a => a.GetName().Name!).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var readers = assemblies.SelectMany(ConfigurationConsumerScan.ConfigurationReaders).ToList();
        var version = AutomateVersion(assemblies);
        var pairs = readers.GroupBy(r => (r.Type, r.Access)).OrderBy(g => g.Key.Type, StringComparer.Ordinal).ThenBy(g => g.Key.Access, StringComparer.Ordinal).ToList();

        output.WriteLine($"Automate {version}; scanned: " + string.Join(", ", scanned));
        foreach (var pair in pairs)
        {
            output.WriteLine($"PAIR {pair.Key.Type} | {pair.Key.Access} | {string.Join(", ", pair.Select(r => r.Member).Distinct().OrderBy(m => m, StringComparer.Ordinal))}");
        }

        // Positive controls: every Automate assembly a site runs was loaded, the signature scan sees internal
        // constructors, and the IL scan sees the service-locator read that the signature scan missed.
        Assert.Contains("Umbraco.Automate.Core", scanned);
        Assert.Contains("Umbraco.Automate.Web", scanned);
        Assert.Contains("Umbraco.Automate.Persistence", scanned);
        Assert.Contains("Umbraco.Automate.Startup", scanned);
        Assert.Contains(pairs, g => g.Key.Type == "Umbraco.Automate.Core.Settings.ConfigurationReferenceResolver");
        Assert.Contains(readers, r => r.Access.Contains("GetRequiredService", StringComparison.Ordinal) && r.Access.Contains("IConfiguration", StringComparison.Ordinal));

        var applicable = PinnedConfigurationConsumers.Where(p => p.AppliesTo?.Invoke(version) ?? true).ToList();
        var unpinned = pairs.Where(g => !applicable.Any(p => p.Type == g.Key.Type && p.Access == g.Key.Access))
            .Select(g => $"{g.Key.Type} ({string.Join(", ", g.Select(r => r.Member).Distinct().OrderBy(m => m, StringComparer.Ordinal))}: {g.Key.Access})")
            .ToList();
        Assert.True(unpinned.Count == 0, $"Unpinned Umbraco.Automate types, methods or accesses that reach configuration on Automate {version}: " + string.Join(" | ", unpinned));

        var stale = applicable.Where(p => !pairs.Any(g => g.Key.Type == p.Type && g.Key.Access == p.Access)).Select(p => $"{p.Type} ({p.Access})").ToList();
        Assert.True(stale.Count == 0, $"Pinned configuration readers that no longer read configuration on Automate {version}: " + string.Join(" | ", stale));

        // AutomateOptions binds from Umbraco:Automate, the parent of the credential section, so a catch-all member on
        // any bound type would copy a package's credentials into memory. A binding whose type is not a generic
        // argument cannot be checked, so it fails too.
        Assert.Equal(BoundTypeScan.SampleExpected, BoundTypeScan.Samples.Select(t => BoundTypeScan.CatchAllMembers(t).Count).ToArray());
        var bound = readers.SelectMany(r => r.BoundTypes).Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        output.WriteLine("Bound types: " + string.Join(", ", bound.Select(t => t.Name)));
        Assert.Contains(typeof(AutomateOptions), bound);
        var unknownBindings = readers.Where(r => r.BoundTypeUnknown).Select(r => $"{r.Type} ({r.Member}: {r.Access})").Distinct().ToList();
        Assert.True(unknownBindings.Count == 0, "Binding calls in Umbraco.Automate.* whose bound type is not statically known: " + string.Join(" | ", unknownBindings));
        var catchAlls = bound.Select(t => (Type: t, Members: BoundTypeScan.CatchAllMembers(t))).Where(b => b.Members.Count > 0)
            .Select(b => $"{b.Type.FullName}: {string.Join(", ", b.Members)}")
            .ToList();
        Assert.True(catchAlls.Count == 0, "Options types Umbraco.Automate.* binds from configuration have catch-all members that would take a credential: " + string.Join(" | ", catchAlls));
    }

    // The Automate version under test, from Umbraco.Automate.Core's informational version (build metadata and any
    // prerelease suffix dropped), so a pin can be scoped to the releases it was reviewed on.
    private static Version AutomateVersion(IEnumerable<Assembly> assemblies)
    {
        var core = assemblies.Single(a => a.GetName().Name == "Umbraco.Automate.Core");
        var text = core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? core.GetName().Version!.ToString();
        return Version.Parse(text.Split('+', '-')[0]);
    }

    // Task 0's security condition: the ${ } binding syntax must have no path to configuration. The step runs
    // Automate's real binding resolver over binding data built by Automate's own builder, in the executor's order:
    // $ references first, then bindings.
    [Fact]
    [Trait("ContractId", "I08")]
    public async Task BindingSyntax_NeverResolvesPackageKey()
    {
        var (host, package, _) = CreateHost();
        var marker = $"BINDING-OK-{Guid.NewGuid():N}";
        var key = PackageKey(SamplePart);
        var bindingData = host.BuildBindingData(
            new Dictionary<string, object?> { ["x"] = marker, ["ref"] = "$" + key, ["key"] = key },
            new Dictionary<string, object?> { ["y"] = marker });
        output.WriteLine("Binding roots: " + string.Join(", ", bindingData.Keys));

        // Positive control: a binding against data the builder holds does resolve, in the URL and in a header.
        var control = await host.RunHttpStepAsync(
            "https://x.example/p?t=${ trigger.x }&s=${ steps.prior.y }&p=${ previous.y }&l=${ loop.index }",
            "${ trigger.x }",
            bindingData);
        Assert.True(control.Error is null, control.Error);
        var sent = Assert.Single(control.Sent);
        Assert.Contains($"t={marker}&s={marker}&p={marker}&l=0", sent.Text);
        Assert.Contains($"X-Test={marker}", sent.Text);

        var roots = bindingData.Keys.Concat(["config", "configuration", "env", "environment", "settings", "secrets", "variables", "app", "Umbraco"]).Distinct(StringComparer.OrdinalIgnoreCase);
        var expressions = new List<string> { "${ trigger.ref }", "$${ trigger.key }" };
        foreach (var part in Parts)
        {
            var target = PackageKey(part);
            foreach (var path in new[] { target, target.Replace(":", "__"), target.Replace(':', '.') })
            {
                expressions.Add("${ " + path + " }");
                expressions.AddRange(roots.Select(root => "${ " + root + "." + path + " }"));
                expressions.AddRange(roots.Select(root => "${" + root + "." + path + "}"));
            }
        }

        var runs = 0;
        foreach (var expression in expressions)
        {
            foreach (var (shape, url, header) in Shapes(expression))
            {
                AssertNoSentinel(package, await host.RunHttpStepAsync(url, header, bindingData), $"'{expression}' as {shape}");
                runs++;
            }
        }

        output.WriteLine($"{expressions.Count} expressions, {runs} step runs, no package value produced or sent.");
    }

    // The store's guard must be at least as strict as core's resolver: whenever core would hand a step the
    // package value for an allow-list entry, the store must report that entry. The store may be stricter.
    [Fact]
    [Trait("ContractId", "I09")]
    public void Guard_AgreesWithRealResolver()
    {
        var segments = Prefix.Split(':');
        var parent = string.Join(':', segments[..^1]);
        var last = segments[^1];
        var corpus = ContractEntries.Exposing(Prefix)
            .Concat(ContractEntries.NotExposing(Prefix))
            .Concat(
            [
                Prefix + ":",
                Prefix + " ",
                Prefix.ToLowerInvariant(),
                ":" + segments[0],
                "Umbraco::Automate",
                parent + ":" + last[..Math.Min(4, last.Length - 1)],
                parent + ":",
                Prefix.Replace(":", "__"),
            ])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var coreLeaked = 0;
        foreach (var entry in corpus)
        {
            var options = new AutomateOptions();
            options.AllowedConfigurationKeyPrefixes = [.. options.AllowedConfigurationKeyPrefixes, entry];
            var (host, package, _) = CreateHost(options);

            var leaks = Parts.Any(part =>
                ResolvesTo(host, "$" + PackageKey(part), package) || ResolvesTo(host, "x $" + PackageKey(part) + " y", package));
            var store = CreateStore(host.Configuration, new TestOptionsMonitor<AutomateOptions>(options), Options.Create(options));
            var reported = store.GetExposingPrefixes().Contains(entry);
            output.WriteLine($"'{entry}': core resolves the package key = {leaks}, store reports it = {reported}");

            if (leaks)
            {
                coreLeaked++;
                Assert.True(reported, $"Core's resolver reads {Prefix} with the entry '{entry}', but the store does not report it.");
            }
        }

        // Positive control: the corpus contains entries core does resolve, so the comparison is not vacuous.
        Assert.True(coreLeaked > 0, "No corpus entry made core resolve the package key; the differential test saw nothing.");
    }

    private static bool ResolvesTo(AutomateResolverHost host, string value, string sentinel)
    {
        try
        {
            return host.Resolver.Resolve(value, typeof(string), true)?.ToString()?.Contains(sentinel) ?? false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>One place a method body reaches configuration. Member is for the message only: lambda and state machine names change between builds, so a pin is keyed on type and access.</summary>
internal sealed record ConfigurationReader(string Type, string Member, string Access, IReadOnlyList<Type> BoundTypes, bool BoundTypeUnknown);

/// <summary>Finds where Umbraco.Automate.* reaches configuration: members whose signatures carry it, and method bodies that call, bind or load a token for it (I07).</summary>
internal static class ConfigurationConsumerScan
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Every constructor or method parameter, field and property whose type is, or wraps in a generic argument,
    /// an <see cref="IConfiguration"/> (which covers the root, manager, section and ConfigurationManager).
    /// </summary>
    public static IEnumerable<(string Type, string Member)> Consumers(Assembly assembly)
    {
        foreach (var type in TypesOf(assembly))
        {
            var owner = Outermost(type).FullName!;
            foreach (var method in type.GetConstructors(All).Cast<MethodBase>().Concat(type.GetMethods(All)))
            {
                if (method.GetParameters().Any(p => Carries(p.ParameterType)))
                {
                    yield return (owner, $"{(method.IsConstructor ? "ctor" : method.Name)}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                }
            }

            foreach (var field in type.GetFields(All).Where(f => Carries(f.FieldType)))
            {
                yield return (owner, "field " + field.Name);
            }

            foreach (var property in type.GetProperties(All).Where(p => Carries(p.PropertyType)))
            {
                yield return (owner, "property " + property.Name);
            }
        }
    }

    /// <summary>
    /// Everything <see cref="Consumers"/> finds, plus every method body in the assembly whose IL calls, creates a
    /// delegate to, or loads a token (ldtoken: typeof, and the method and field handles expression trees use) that
    /// reaches configuration: a member declared on, taking or returning an <see cref="IConfiguration"/> (which covers
    /// GetSection, GetValue, Bind, GetConnectionString, indexers and IUmbracoBuilder.Config), a generic instantiation
    /// over one (GetRequiredService, GetService), an options binding call (Get, Bind, Configure, BindConfiguration)
    /// or an environment-variable read. A target or token the runtime cannot resolve is reported rather than
    /// skipped, so the scan fails closed. It is a static scan: reflection invoke, <c>dynamic</c>, <c>calli</c> and
    /// readers outside Umbraco.Automate.* are not covered.
    /// </summary>
    public static IEnumerable<ConfigurationReader> ConfigurationReaders(Assembly assembly)
    {
        foreach (var consumer in Consumers(assembly))
        {
            yield return new(consumer.Type, consumer.Member, "declares " + consumer.Member, [], false);
        }

        foreach (var (type, member, target) in IlScan.Calls(assembly))
        {
            var owner = Outermost(type).FullName!;
            if (target is null)
            {
                yield return new(owner, member, "call target could not be resolved", [], false);
            }
            else if (ReachesConfiguration(target))
            {
                yield return Reader(owner, member, "calls " + Describe(target), target);
            }
        }

        foreach (var (type, member, token) in IlScan.MemberTokens(assembly))
        {
            var owner = Outermost(type).FullName!;
            switch (token)
            {
                case null:
                    yield return new(owner, member, "ldtoken could not be resolved", [], false);
                    break;
                case Type loaded when Carries(loaded):
                    yield return new(owner, member, "loads type token " + loaded.Name, [], false);
                    break;
                case MethodBase method when ReachesConfiguration(method):
                    yield return Reader(owner, member, "loads method token " + Describe(method), method);
                    break;
                case FieldInfo field when Carries(field.FieldType):
                    yield return new(owner, member, "loads field token " + field.Name, [], false);
                    break;
            }
        }
    }

    private static ConfigurationReader Reader(string owner, string member, string access, MethodBase target)
    {
        if (!IsBinding(target))
        {
            return new(owner, member, access, [], false);
        }

        // A binding call whose target type is not a generic argument (Bind(object), Get(Type)) cannot be checked for catch-alls.
        var bound = target.IsGenericMethod ? target.GetGenericArguments().Where(a => !a.IsGenericParameter).ToList() : [];
        return new(owner, member, access, bound, bound.Count == 0 || (target.IsGenericMethod && target.GetGenericArguments().Any(a => a.IsGenericParameter)));
    }

    // Calls that copy configuration into a typed object, whose members decide what a parent section can carry.
    private static bool IsBinding(MethodBase target) => (target.DeclaringType?.Name, target.Name) switch
    {
        ("ConfigurationBinder", "Get" or "Bind") => true,
        ("OptionsConfigurationServiceCollectionExtensions", "Configure") => true,
        ("OptionsBuilderConfigurationExtensions", "Bind" or "BindConfiguration") => true,
        _ => false,
    };

    private static bool ReachesConfiguration(MethodBase target)
    {
        if (IsBinding(target))
        {
            return true;
        }

        // Environment variables are a configuration source, so reading them directly is a second route to a credential.
        if (target.DeclaringType == typeof(Environment) && target.Name is "GetEnvironmentVariable" or "GetEnvironmentVariables" or "ExpandEnvironmentVariables")
        {
            return true;
        }

        if (target.DeclaringType is { } declaring && Carries(declaring))
        {
            return true;
        }

        if (target.GetParameters().Any(p => Carries(p.ParameterType)) || (target is MethodInfo method && Carries(method.ReturnType)))
        {
            return true;
        }

        return target.IsGenericMethod && target.GetGenericArguments().Any(Carries);
    }

    private static string Describe(MethodBase target)
    {
        var generics = target.IsGenericMethod ? "<" + string.Join(",", target.GetGenericArguments().Select(a => a.Name)) + ">" : string.Empty;
        return $"{target.DeclaringType?.Name}.{target.Name}{generics}";
    }

    public static bool Carries(Type type)
    {
        if (type.HasElementType)
        {
            return Carries(type.GetElementType()!);
        }

        return typeof(IConfiguration).IsAssignableFrom(type)
            || (type.IsGenericType && type.GetGenericArguments().Any(Carries));
    }

    // Lambdas, closures and state machines are reported against the type whose code they came from.
    public static Type Outermost(Type type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    public static Type[] TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToArray();
        }
    }

    /// <summary>Everything named Umbraco.Automate.* that the test project loads or ships in its output folder.</summary>
    public static IEnumerable<Assembly> LoadAutomateAssemblies()
    {
        var found = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        void Add(Assembly assembly)
        {
            var name = assembly.GetName().Name!;
            if (name.StartsWith("Umbraco.Automate", StringComparison.OrdinalIgnoreCase) && found.TryAdd(name, assembly))
            {
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    TryLoad(reference);
                }
            }
        }

        void TryLoad(AssemblyName name)
        {
            if (name.Name!.StartsWith("Umbraco.Automate", StringComparison.OrdinalIgnoreCase) && !found.ContainsKey(name.Name))
            {
                try
                {
                    Add(Assembly.Load(name));
                }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    // Not part of this test project's dependency graph; I07 asserts the ones that must be.
                }
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Add(assembly);
        }

        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "Umbraco.Automate.*.dll"))
        {
            TryLoad(AssemblyName.GetAssemblyName(file));
        }

        return found.Values;
    }
}

/// <summary>
/// Runs Automate's public registration entry point, <c>AddUmbracoAutomate</c> in Umbraco.Automate.Startup, on a
/// real <see cref="UmbracoBuilder"/>. It is found by reflection so the suite fails loudly, rather than not
/// compiling, when the test project lacks the Startup package.
/// </summary>
internal static class AutomateRegistration
{
    public static IUmbracoBuilder AddUmbracoAutomate(IServiceCollection services, IConfiguration configuration)
    {
        var builder = CreateBuilder(services, configuration);
        var startup = Assembly.Load("Umbraco.Automate.Startup");
        var method = startup.GetType("Umbraco.Automate.Extensions.UmbracoBuilderExtensions", throwOnError: true)!
            .GetMethod("AddUmbracoAutomate", BindingFlags.Public | BindingFlags.Static, [typeof(IUmbracoBuilder)])
            ?? throw new InvalidOperationException("Umbraco.Automate.Startup no longer has AddUmbracoAutomate(IUmbracoBuilder).");
        method.Invoke(null, [builder]);
        return builder;
    }

    /// <summary>
    /// A real builder over a type finder that finds nothing: registration code runs as on a site, and the
    /// attribute-discovered collections it defers to the type loader stay empty.
    /// </summary>
    public static IUmbracoBuilder CreateBuilder(IServiceCollection services, IConfiguration configuration) =>
        new UmbracoBuilder(services, configuration, new TypeLoader(new EmptyTypeFinder(), NullLogger<TypeLoader>.Instance, []));

    private sealed class EmptyTypeFinder : ITypeFinder
    {
        public IEnumerable<Assembly> AssembliesToScan => [];

        public Type? GetTypeByName(string name) => null;

        public IEnumerable<Type> FindClassesOfTypeWithAttribute(Type assignTypeFrom, Type attributeType, IEnumerable<Assembly>? assemblies = null, bool onlyConcreteClasses = true) => [];

        public IEnumerable<Type> FindClassesOfType(Type assignTypeFrom, IEnumerable<Assembly>? assemblies = null, bool onlyConcreteClasses = true) => [];

        public IEnumerable<Type> FindClassesWithAttribute(Type attributeType, IEnumerable<Assembly>? assemblies = null, bool onlyConcreteClasses = true) => [];
    }
}

/// <summary>One request as the handler saw it: method, URL, headers and body in one string for substring checks.</summary>
internal sealed record CapturedRequest(string Text);

/// <summary>What one step run produced: its failure text (null on success), its resolved settings as JSON, and what it sent.</summary>
internal sealed record StepOutcome(string? Error, string Settings, IReadOnlyList<CapturedRequest> Sent);

/// <summary>Records every outbound request; never touches the network.</summary>
internal sealed class ContractCapturingHandler(List<CapturedRequest> captured) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = string.Join(";", request.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"));
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        captured.Add(new CapturedRequest($"{request.Method} {request.RequestUri} {headers} {body}"));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
    }
}

/// <summary>
/// Builds Automate's real configuration-reference resolver, binding resolver and built-in HTTP Request action by
/// reflection on the internal types (credential-helper-contract section 5.2 records why: Automate's own
/// registration needs a running site and its testing host mocks the resolver). The route is the same on every
/// line; a release that renames a type or constructor makes Create throw rather than pass.
/// </summary>
internal sealed class AutomateResolverHost
{
    private readonly HttpRequestAction action;
    private readonly object bindingResolver;
    private readonly MethodInfo resolveBindings;
    private readonly List<CapturedRequest> captured;

    private AutomateResolverHost(IConfiguration configuration, IConfigurationReferenceResolver resolver, HttpRequestAction action, object bindingResolver, List<CapturedRequest> captured)
    {
        Configuration = configuration;
        Resolver = resolver;
        this.action = action;
        this.bindingResolver = bindingResolver;
        resolveBindings = bindingResolver.GetType().GetMethod("ResolveBindings", [typeof(object), typeof(IReadOnlyDictionary<string, object?>)])
            ?? throw new InvalidOperationException("SettingsBindingResolver.ResolveBindings(object, IReadOnlyDictionary<string, object?>) is missing.");
        this.captured = captured;
    }

    public IConfiguration Configuration { get; }

    public IConfigurationReferenceResolver Resolver { get; }

    private static Assembly Core => typeof(ActionInfrastructure).Assembly;

    public static AutomateResolverHost Create(IConfiguration configuration, AutomateOptions options, ExecutionOptions? execution = null)
    {
        var resolver = (IConfigurationReferenceResolver)Activator.CreateInstance(
            Core.GetType("Umbraco.Automate.Core.Settings.ConfigurationReferenceResolver", throwOnError: true)!,
            configuration,
            Options.Create(options))!;
        var models = (IEditableModelResolver)Activator.CreateInstance(
            Core.GetType("Umbraco.Automate.Core.Settings.EditableModelResolver", throwOnError: true)!,
            resolver)!;

        // Every filter Automate ships, as its collection builder discovers them on a site.
        var filters = Core.GetTypes()
            .Where(t => typeof(IBindingFilter).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
            .Select(t => (IBindingFilter)Activator.CreateInstance(t, nonPublic: true)!)
            .ToList();
        var evaluator = new BindingEvaluator(new BindingFilterCollection(() => filters));
        var bindingResolver = Activator.CreateInstance(
            Core.GetType("Umbraco.Automate.Core.Bindings.SettingsBindingResolver", throwOnError: true)!,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [evaluator],
            null)!;

        var captured = new List<CapturedRequest>();
        var services = new ServiceCollection();
        services.AddHttpClient("UmbracoAutomate").ConfigurePrimaryHttpMessageHandler(() => new ContractCapturingHandler(captured));
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var action = new HttpRequestAction(new ActionInfrastructure(models), factory, Options.Create(execution ?? new ExecutionOptions()));
        return new AutomateResolverHost(configuration, resolver, action, bindingResolver, captured);
    }

    /// <summary>
    /// Binding data from Automate's own BindingDataBuilder: the given trigger output, one prior step (alias
    /// "prior", also the previous step) with the given output, and the first iteration of a loop.
    /// </summary>
    public IReadOnlyDictionary<string, object?> BuildBindingData(Dictionary<string, object?> triggerOutput, Dictionary<string, object?> stepOutput)
    {
        var stepId = Guid.NewGuid();
        var data = new AutomationWorkflowData
        {
            RunId = Guid.NewGuid(),
            AutomationId = Guid.NewGuid(),
            TriggerOutput = triggerOutput,
            StepOutputs = new() { [stepId] = stepOutput },
            StepAliases = new() { [stepId] = "prior" },
            LastCompletedStepId = stepId,
        };

        var iterationType = Core.GetType("Umbraco.Automate.Core.Execution.ControlFlow.ForEachIterationContext", throwOnError: true)!;
        var iteration = Activator.CreateInstance(iterationType, "item", 0, Guid.NewGuid(), null);
        var build = Core.GetType("Umbraco.Automate.Core.Execution.BindingDataBuilder", throwOnError: true)!
            .GetMethod("Build", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BindingDataBuilder.Build is missing.");
        var parameters = build.GetParameters();
        var arguments = parameters.Select(p => p.ParameterType == typeof(AutomationWorkflowData) ? data
            : p.ParameterType == iterationType ? iteration
            : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
            : p.HasDefaultValue ? p.DefaultValue : null).ToArray();

        return (IReadOnlyDictionary<string, object?>)build.Invoke(null, arguments)!;
    }

    /// <summary>
    /// Runs the HTTP Request action as the step executor does: $ references are resolved by ResolveSettings,
    /// then ${ } bindings (when binding data is given), then the action runs.
    /// </summary>
    public async Task<StepOutcome> RunHttpStepAsync(string url, string? headerValue, IReadOnlyDictionary<string, object?>? bindingData = null)
    {
        var raw = new Dictionary<string, object?> { ["url"] = url, ["method"] = "GET" };
        if (headerValue is not null)
        {
            raw["headers"] = new List<Dictionary<string, object?>> { new() { ["key"] = "X-Test", ["value"] = headerValue } };
        }

        var before = captured.Count;
        object? settings = null;
        string? error;
        try
        {
            settings = action.ResolveSettings(raw);
            if (bindingData is not null && settings is not null)
            {
                resolveBindings.Invoke(bindingResolver, [settings, bindingData]);
            }

            var context = new ActionContext
            {
                AutomationId = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                StepId = Guid.NewGuid(),
                ActionAlias = action.Alias,
                Settings = settings,
                Action = action,
            };
            var result = await action.ExecuteAsync(context, CancellationToken.None);
            error = result.Status == ActionResultStatus.Success
                ? null
                : $"{result.Status}: {result.Exception?.GetType().Name} {result.Exception?.Message} {result.Reason} {JsonSerializer.Serialize(result.OutputData)}";
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException { InnerException: { } wrapped } ? wrapped : ex;
            error = inner.GetType().Name + ": " + inner.Message;
        }

        return new StepOutcome(error, settings is null ? string.Empty : JsonSerializer.Serialize(settings, settings.GetType()), captured.Skip(before).ToList());
    }
}
