// credential-helper-contract: 1.0.0
using System.CodeDom.Compiler;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Umbraco.Automate.Core.Configuration;
using Xunit;

namespace CredentialHelperContract;

/// <summary>
/// Generic conformance suite for the credential store (credential-helper-contract section 5.1).
/// Copied verbatim into every consumer package; only the subclass is written per package.
/// The class is generic over the parts enum only, because xUnit v2 needs public test classes and a
/// public class cannot name the package's internal store type. The store is held through the
/// internal <see cref="ICredentialStore{TPart}"/> instead.
/// </summary>
public abstract class CredentialStoreContractTests<TPart>
    where TPart : struct, Enum
{
    public const string ContractVersion = "1.0.0";

    // The canonical hash of the store source and both suites. Never change it locally: re-copy the canonical files instead.
    public const string CanonicalHash = "39aebb90bfaa7d21e56711a62910483467639d58f7a5b1c38b1019804a71cdba";

    private const string NameError = "Credential name must be 1 to 32 letters or digits.";
    private const string UnknownPartError = "Unknown credential part.";

    protected abstract string Prefix { get; }

    protected abstract string Namespace { get; }

    protected abstract string StoreClassName { get; }

    protected abstract string PartEnumName { get; }

    internal abstract ICredentialStore<TPart> CreateStore(
        IConfiguration configuration,
        IOptionsMonitor<AutomateOptions> options,
        IOptions<AutomateOptions> startupOptions);

    internal abstract object CreateStartupCheck(ICredentialStore<TPart> store, ILoggerFactory loggerFactory);

    internal abstract Task RunStartupCheck(object check);

    /// <summary>
    /// Package types outside the store file that take <see cref="IConfiguration"/>, call a member that returns
    /// one, or name a parent section of <see cref="Prefix"/>, by full name, each with the reason it cannot read
    /// the credential section (C31).
    /// </summary>
    protected abstract IReadOnlyDictionary<string, string> PinnedConfigurationReaders { get; }

    /// <summary>The options types the package binds from a parent section of <see cref="Prefix"/> (C31).</summary>
    protected abstract IReadOnlyList<Type> ParentSectionBoundTypes { get; }

    /// <summary>
    /// Package types outside the store file that call <c>KeyFor</c>, by full name, each with the message it builds
    /// (C28). KeyFor is for messages only; any other use would index configuration without the guard.
    /// </summary>
    protected abstract IReadOnlyDictionary<string, string> PinnedKeyForCallers { get; }

    private static IEnumerable<TPart> Parts => Enum.GetValues<TPart>();

    private string KeyOf(string name, TPart part) => $"{Prefix}:{name}:{part}";

    private string MissingError(string key) => $"No value is configured at {key}.";

    private string GuardError(string entry) =>
        $"Refused: Umbraco:Automate:AllowedConfigurationKeyPrefixes contains '{entry}', which exposes {Prefix}. Remove that entry.";

    // Default Automate entries plus extras, as a site that widened the list would have it.
    private static AutomateOptions OptionsWith(params string[] extra)
    {
        var options = new AutomateOptions();
        options.AllowedConfigurationKeyPrefixes = [.. options.AllowedConfigurationKeyPrefixes, .. extra];
        return options;
    }

    private sealed record Rig(
        ICredentialStore<TPart> Store,
        RecordingConfiguration Config,
        TestOptionsMonitor<AutomateOptions> Monitor,
        AutomateOptions Startup);

    // monitorExtra: entries the live options hold. startupExtra: entries the startup snapshot held.
    private Rig Create(string[]? monitorExtra = null, string[]? startupExtra = null) =>
        Create(OptionsWith(monitorExtra ?? []), OptionsWith(startupExtra ?? []));

    private Rig Create(AutomateOptions live, AutomateOptions startup)
    {
        var config = new RecordingConfiguration();
        var monitor = new TestOptionsMonitor<AutomateOptions>(live);
        return new Rig(CreateStore(config, monitor, Options.Create(startup)), config, monitor, startup);
    }

    private void SetAll(Rig rig, string name, Func<TPart, string> value)
    {
        foreach (var part in Parts)
        {
            rig.Config.Set(KeyOf(name, part), value(part));
        }
    }

    private async Task<IReadOnlyList<CapturedLog>> RunCheck(Rig rig)
    {
        var logger = new CapturingLogger();
        await RunStartupCheck(CreateStartupCheck(rig.Store, new CapturingLoggerFactory(logger)));
        return logger.Entries;
    }

    [Fact]
    [Trait("ContractId", "C01")]
    public void TryGet_ReadsEveryPart()
    {
        var rig = Create();
        SetAll(rig, "agency", part => $"VALUE-{part}");

        foreach (var part in Parts)
        {
            Assert.True(rig.Store.TryGet("agency", part, out var value, out var error), $"{part}: {error}");
            Assert.Equal($"VALUE-{part}", value);
            Assert.Null(error);
        }
    }

    [Fact]
    [Trait("ContractId", "C02")]
    public void TryGet_ReadsPerCall_RotationWithoutRestart()
    {
        var rig = Create();

        foreach (var part in Parts)
        {
            rig.Config.Set(KeyOf("agency", part), "OLD-VALUE");
            Assert.True(rig.Store.TryGet("agency", part, out var first, out _));
            Assert.Equal("OLD-VALUE", first);

            rig.Config.Set(KeyOf("agency", part), "NEW-VALUE");
            rig.Config.Reload();

            Assert.True(rig.Store.TryGet("agency", part, out var second, out _));
            Assert.Equal("NEW-VALUE", second);
            Assert.NotEqual("OLD-VALUE", second);
        }
    }

    [Fact]
    [Trait("ContractId", "C03")]
    public void Name_IsTrimmedAndCaseInsensitive()
    {
        var rig = Create();
        SetAll(rig, "agency", part => $"VALUE-{part}");

        foreach (var part in Parts)
        {
            foreach (var spelling in new[] { "Agency", " AGENCY ", "agency\n", "\tagency" })
            {
                Assert.True(rig.Store.TryGet(spelling, part, out var value, out var error), $"'{spelling}' {part}: {error}");
                Assert.Equal($"VALUE-{part}", value);
            }
        }

        Assert.All(rig.Config.Reads, key => Assert.StartsWith($"{Prefix}:agency:", key));
    }

    // Under tr-TR, culture-sensitive lower-casing turns "I" into the dotless U+0131, which the name rule rejects
    // and which would name a different key. The name must be lower-cased invariantly.
    [Fact]
    [Trait("ContractId", "C20")]
    public void Name_LowerCasedInvariantly_UnderTurkishCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var rig = Create();
            SetAll(rig, "index", part => $"VALUE-{part}");

            foreach (var part in Parts)
            {
                Assert.True(rig.Store.TryGet("INDEX", part, out var value, out var error), $"{part}: {error}");
                Assert.Equal($"VALUE-{part}", value);
            }

            Assert.All(rig.Config.Reads, key => Assert.StartsWith($"{Prefix}:index:", key));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Theory]
    [Trait("ContractId", "C04")]
    [InlineData("..")]
    [InlineData("a:b")]
    [InlineData("a__b")]
    [InlineData("a-b")]
    [InlineData("a_b")]
    [InlineData("a b")]
    [InlineData("a.b")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 33 characters
    [InlineData("a\nb")]
    [InlineData("ag\u00E9ncy")]
    [InlineData("\u0430gency")] // Cyrillic a
    [InlineData("\u212Aey")] // Kelvin sign, which lower-cases to ASCII k
    [InlineData("\uFF11")] // full-width 1
    [InlineData("agency\u200B")] // zero-width space, which Trim keeps
    public void Name_Rejected_NoConfigRead(string? name)
    {
        var rig = Create();
        SetAll(rig, "agency", part => $"VALUE-{part}");

        foreach (var part in Parts)
        {
            Assert.False(rig.Store.TryGet(name, part, out var value, out var error));
            Assert.Null(value);
            Assert.Equal(NameError, error);
            if (!string.IsNullOrEmpty(name))
            {
                Assert.DoesNotContain(name, error);
            }
        }

        Assert.False(rig.Store.TryNormaliseName(name, out var normalised, out var normaliseError));
        Assert.Null(normalised);
        Assert.Equal(NameError, normaliseError);
        Assert.Empty(rig.Config.Reads);
    }

    [Fact]
    [Trait("ContractId", "C05")]
    public void NameRule_UsesEndOfStringAnchor()
    {
        var store = Create().Store;

        Assert.False(store.MatchesNameRule("agency\n"));
        Assert.True(store.MatchesNameRule("agency"));
        Assert.False(store.MatchesNameRule("Agency"));
    }

    [Fact]
    [Trait("ContractId", "C06")]
    public void Name_32Characters_Accepted()
    {
        var rig = Create();
        var name = new string('a', 32);
        SetAll(rig, name, part => $"VALUE-{part}");

        foreach (var part in Parts)
        {
            Assert.True(rig.Store.TryGet(name, part, out var value, out var error), error);
            Assert.Equal($"VALUE-{part}", value);
        }

        Assert.NotEmpty(rig.Config.Reads);
    }

    [Fact]
    [Trait("ContractId", "C07")]
    public void Missing_ErrorNamesKeyOnly()
    {
        var rig = Create();
        var sentinel = $"SENTINEL-OTHER-{Guid.NewGuid()}";
        rig.Config.Set($"{Prefix}:other:{Parts.First()}", sentinel);
        rig.Config.Set("Some:Other:Key", sentinel);

        foreach (var part in Parts)
        {
            Assert.False(rig.Store.TryGet("agency", part, out var value, out var error));
            Assert.Null(value);
            Assert.Equal(MissingError(rig.Store.KeyFor("agency", part)), error);
            Assert.Equal(MissingError(KeyOf("agency", part)), error);
            Assert.DoesNotContain(sentinel, error);
        }
    }

    [Fact]
    [Trait("ContractId", "C08")]
    public void BlankValue_IsMissing()
    {
        var rig = Create();

        foreach (var blank in new[] { "", "   " })
        {
            foreach (var part in Parts)
            {
                rig.Config.Set(KeyOf("agency", part), blank);
                Assert.False(rig.Store.TryGet("agency", part, out var value, out var error));
                Assert.Null(value);
                Assert.Equal(MissingError(KeyOf("agency", part)), error);
            }
        }
    }

    [Fact]
    [Trait("ContractId", "C09")]
    public void Value_ReturnedExactlyAsStored()
    {
        var rig = Create();
        SetAll(rig, "agency", _ => " v \n");

        foreach (var part in Parts)
        {
            Assert.True(rig.Store.TryGet("agency", part, out var value, out _));
            Assert.Equal(" v \n", value);
        }
    }

    [Fact]
    [Trait("ContractId", "C10")]
    public void Guard_Trips()
    {
        foreach (var entry in ContractEntries.Exposing(Prefix))
        {
            foreach (var source in new[] { "monitor", "snapshot" })
            {
                var rig = source == "monitor" ? Create([entry]) : Create(startupExtra: [entry]);
                SetAll(rig, "agency", part => $"VALUE-{part}");
                var label = $"entry '{entry}' in the {source}";

                foreach (var part in Parts)
                {
                    Assert.False(rig.Store.TryGet("agency", part, out var value, out var error), label);
                    Assert.Null(value);
                    Assert.Equal(GuardError(entry), error);
                }

                Assert.True(rig.Store.TryGetExposingPrefix(out var reported), label);
                Assert.Equal(entry, reported);
                Assert.Contains(entry, rig.Store.GetExposingPrefixes());
                Assert.DoesNotContain(rig.Config.Reads, key => key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    [Trait("ContractId", "C11")]
    public void Guard_DoesNotTrip()
    {
        foreach (var entry in ContractEntries.NotExposing(Prefix))
        {
            var rig = Create([entry], [entry]);
            SetAll(rig, "agency", part => $"VALUE-{part}");

            foreach (var part in Parts)
            {
                Assert.True(rig.Store.TryGet("agency", part, out var value, out var error), $"'{entry}': {error}");
                Assert.Equal($"VALUE-{part}", value);
            }

            Assert.False(rig.Store.TryGetExposingPrefix(out _), entry);
            Assert.Empty(rig.Store.GetExposingPrefixes());
        }
    }

    [Fact]
    [Trait("ContractId", "C12")]
    public void Guard_RunsBeforeNameCheck()
    {
        var rig = Create(["Umbraco:Automate"]);

        Assert.False(rig.Store.TryGet("a:b", Parts.First(), out _, out var error));

        Assert.Equal(GuardError("Umbraco:Automate"), error);
        Assert.Empty(rig.Config.Reads);
    }

    [Fact]
    [Trait("ContractId", "C13")]
    public void Guard_FollowsOptionsChangeWithoutRestart()
    {
        // Live options: a runtime change trips the guard and lifting it (snapshot clean) clears it.
        var live = Create();
        SetAll(live, "agency", part => $"VALUE-{part}");
        var part0 = Parts.First();
        Assert.True(live.Store.TryGet("agency", part0, out _, out _));

        live.Monitor.CurrentValue = OptionsWith("Umbraco:Automate");
        Assert.False(live.Store.TryGet("agency", part0, out _, out var refused));
        Assert.Equal(GuardError("Umbraco:Automate"), refused);

        live.Monitor.CurrentValue = OptionsWith();
        Assert.True(live.Store.TryGet("agency", part0, out _, out _));

        // Core's resolver reads its allow-list once at startup, so an entry the startup snapshot held
        // keeps the store refusing after the live options drop it, until a restart.
        var sticky = Create(["Umbraco:Automate"], ["Umbraco:Automate"]);
        SetAll(sticky, "agency", part => $"VALUE-{part}");
        Assert.False(sticky.Store.TryGet("agency", part0, out _, out _));

        sticky.Monitor.CurrentValue = OptionsWith();
        Assert.False(sticky.Store.TryGet("agency", part0, out _, out var stillRefused));
        Assert.Equal(GuardError("Umbraco:Automate"), stillRefused);

        // A restart builds a fresh store over a clean snapshot.
        var restarted = Create();
        SetAll(restarted, "agency", part => $"VALUE-{part}");
        Assert.True(restarted.Store.TryGet("agency", part0, out _, out _));
    }

    [Fact]
    [Trait("ContractId", "C21")]
    public void Guard_SnapshotExposing_RefusesEvenIfMonitorClean()
    {
        var rig = Create(monitorExtra: [], startupExtra: [Prefix]);
        SetAll(rig, "agency", part => $"VALUE-{part}");

        foreach (var part in Parts)
        {
            Assert.False(rig.Store.TryGet("agency", part, out var value, out var error));
            Assert.Null(value);
            Assert.Equal(GuardError(Prefix), error);
        }

        Assert.True(rig.Store.TryGetExposingPrefix(out var entry));
        Assert.Equal(Prefix, entry);
    }

    [Fact]
    [Trait("ContractId", "C14")]
    public void DefaultAutomateOptions_HaveNoExposingEntry()
    {
        var store = CreateStore(new RecordingConfiguration(), new TestOptionsMonitor<AutomateOptions>(new AutomateOptions()), Options.Create(new AutomateOptions()));

        Assert.False(store.TryGetExposingPrefix(out var entry), entry);
        Assert.Empty(store.GetExposingPrefixes());
    }

    [Fact]
    [Trait("ContractId", "C22")]
    public void GetExposingPrefixes_ListsEveryEntryInOrder_FirstIsTheSingleReport()
    {
        var rig = Create(["Umbraco:Automate", Prefix + ":agency"]);

        var all = rig.Store.GetExposingPrefixes();

        Assert.Equal(["Umbraco:Automate", Prefix + ":agency"], all);
        Assert.True(rig.Store.TryGetExposingPrefix(out var first));
        Assert.Equal(all[0], first);
    }

    // Sites usually hold the same entry in both sources, because both are bound from the same configuration.
    [Fact]
    [Trait("ContractId", "C23")]
    public async Task GetExposingPrefixes_SameEntryInBothSources_ReportedOnce()
    {
        var rig = Create([Prefix], [Prefix]);

        Assert.Equal([Prefix], rig.Store.GetExposingPrefixes());

        var critical = (await RunCheck(rig)).Where(e => e.Level == LogLevel.Critical).ToList();
        Assert.Equal(GuardError(Prefix), Assert.Single(critical).Message);
    }

    [Fact]
    [Trait("ContractId", "C24")]
    public void GetExposingPrefixes_LiveEntriesFirst_ThenSnapshotEntries()
    {
        var parent = ContractEntries.Parents(Prefix).Last();

        Assert.Equal([parent, Prefix], Create([parent], [Prefix]).Store.GetExposingPrefixes());
        Assert.Equal([Prefix, parent], Create([Prefix], [parent]).Store.GetExposingPrefixes());
    }

    [Fact]
    [Trait("ContractId", "C25")]
    public void Guard_NullAllowList_HasNoEntries()
    {
        var live = new AutomateOptions { AllowedConfigurationKeyPrefixes = null! };
        var startup = new AutomateOptions { AllowedConfigurationKeyPrefixes = null! };
        var rig = Create(live, startup);
        SetAll(rig, "agency", part => $"VALUE-{part}");

        Assert.Empty(rig.Store.GetExposingPrefixes());
        Assert.False(rig.Store.TryGetExposingPrefix(out var entry), entry);
        foreach (var part in Parts)
        {
            Assert.True(rig.Store.TryGet("agency", part, out var value, out var error), $"{part}: {error}");
            Assert.Equal($"VALUE-{part}", value);
        }
    }

    // A null entry fails closed like an empty one and is reported as "", so the error never prints "null".
    [Fact]
    [Trait("ContractId", "C26")]
    public void Guard_NullEntry_ReportedAsEmpty()
    {
        foreach (var source in new[] { "monitor", "snapshot" })
        {
            var withNull = new AutomateOptions { AllowedConfigurationKeyPrefixes = [null!] };
            var rig = source == "monitor" ? Create(withNull, new AutomateOptions()) : Create(new AutomateOptions(), withNull);

            Assert.Equal([string.Empty], rig.Store.GetExposingPrefixes());
            Assert.True(rig.Store.TryGetExposingPrefix(out var entry), source);
            Assert.Equal(string.Empty, entry);
            Assert.False(rig.Store.TryGet("agency", Parts.First(), out _, out var error), source);
            Assert.Equal(GuardError(string.Empty), error);
        }
    }

    [Fact]
    [Trait("ContractId", "C15")]
    public async Task StartupCheck_LogsCriticalOncePerExposingEntry_NoValues()
    {
        var sentinel = $"SENTINEL-VALUE-{Guid.NewGuid()}";
        var entries = new[] { "Umbraco:Automate", Prefix + ":agency" };
        var rig = Create(entries);
        SetAll(rig, "agency", _ => sentinel);

        var logged = await RunCheck(rig);

        var critical = logged.Where(e => e.Level == LogLevel.Critical).ToList();
        Assert.Equal(2, critical.Count);
        for (var i = 0; i < entries.Length; i++)
        {
            Assert.Equal(GuardError(entries[i]), critical[i].Message);
        }

        Assert.DoesNotContain(logged, e => e.Text.Contains(sentinel));
        Assert.Empty(rig.Config.Reads);

        var clean = await RunCheck(Create());
        Assert.DoesNotContain(clean, e => e.Level > LogLevel.Debug);
    }

    [Fact]
    [Trait("ContractId", "C27")]
    public async Task StartupCheck_SnapshotOnlyEntry_IsLogged()
    {
        var logged = await RunCheck(Create(startupExtra: ["Umbraco"]));

        Assert.Equal(GuardError("Umbraco"), Assert.Single(logged, e => e.Level == LogLevel.Critical).Message);
    }

    // The store and its startup check hold nothing but their injected singletons (pitfall P2). Static state
    // would outlive a configuration reload and could cache a value, so any static field or property fails here,
    // whether written by hand or generated for an auto-property. Only constants and the members the
    // [GeneratedRegex] source generator emits are allowed.
    [Fact]
    [Trait("ContractId", "C16")]
    public void Store_HoldsNoState()
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var rig = Create();
        var store = rig.Store.GetType();
        var check = CreateStartupCheck(rig.Store, new CapturingLoggerFactory(new CapturingLogger())).GetType();

        Assert.Equal(
            new[] { typeof(IConfiguration), typeof(IOptionsMonitor<AutomateOptions>), typeof(IOptions<AutomateOptions>) }.OrderBy(t => t.FullName),
            store.GetFields(All).Where(f => !f.IsStatic).Select(f => f.FieldType).OrderBy(t => t.FullName));
        Assert.Equal(
            new[] { store, typeof(ILogger<>).MakeGenericType(check) }.OrderBy(t => t.FullName),
            check.GetFields(All).Where(f => !f.IsStatic).Select(f => f.FieldType).OrderBy(t => t.FullName));

        var offenders = new List<string>();
        foreach (var type in WithNestedTypes(store).Concat(WithNestedTypes(check)))
        {
            offenders.AddRange(type.GetFields(All)
                .Where(f => f.IsStatic && !f.IsLiteral && !IsRegexGenerated(f))
                .Select(f => $"static field {type.FullName}.{f.Name}"));
            offenders.AddRange(type.GetProperties(All)
                .Where(p => (p.GetMethod ?? p.SetMethod)!.IsStatic && !IsRegexGenerated(p))
                .Select(p => $"static property {type.FullName}.{p.Name}"));
        }

        Assert.True(offenders.Count == 0, "Static state in the credential store file: " + string.Join(", ", offenders));
    }

    private static IEnumerable<Type> WithNestedTypes(Type type) =>
        new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNestedTypes));

    private static bool IsRegexGenerated(MemberInfo member) =>
        member.GetCustomAttribute<GeneratedCodeAttribute>()?.Tool == "System.Text.RegularExpressions.Generator";

    // Contract section 4 item 11: nothing outside the store file may name the package's section. A constant is
    // compiled into every caller as a string literal, so scanning IL for that literal finds every reader. KeyFor
    // builds the same key, so a call to it outside the store file is a second route to the section unless it is
    // pinned as message-only.
    [Fact]
    [Trait("ContractId", "C28")]
    public void OnlyTheStoreFile_NamesThePrefixOrCallsKeyFor()
    {
        var rig = Create();
        var store = rig.Store.GetType();
        var check = CreateStartupCheck(rig.Store, new CapturingLoggerFactory(new CapturingLogger())).GetType();
        var allowed = WithNestedTypes(store).Concat(WithNestedTypes(check)).ToHashSet();
        var spellings = new[] { Prefix, Prefix.Replace(":", "__") };

        var namers = IlScan.Strings(store.Assembly)
            .Where(found => spellings.Any(s => found.Value.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Positive control: the scan reads the store's own IL.
        Assert.Contains(namers, found => found.Type == store);
        var outside = namers.Where(found => !allowed.Contains(found.Type)).Select(found => $"{found.Type.FullName}.{found.Member}").Distinct().ToList();
        Assert.True(outside.Count == 0, "Types outside the credential store file name its prefix: " + string.Join(", ", outside));

        var calls = IlScan.Calls(store.Assembly).ToList();
        Assert.True(calls.All(c => c.Target is not null), "Unresolved call targets: " + string.Join(", ", calls.Where(c => c.Target is null).Select(c => $"{c.Type.FullName}.{c.Member}")));
        var keyForCalls = calls.Where(c => IsKeyFor(c.Target!, store)).ToList();

        // Positive control: TryGet's own call to KeyFor is seen.
        Assert.Contains(keyForCalls, c => c.Type == store);
        var unpinnedCallers = keyForCalls
            .Where(c => !allowed.Contains(c.Type) && !PinnedKeyForCallers.ContainsKey(ConfigurationConsumerScan.Outermost(c.Type).FullName!))
            .Select(c => $"{c.Type.FullName}.{c.Member}")
            .Distinct()
            .ToList();
        Assert.True(unpinnedCallers.Count == 0, "Types outside the credential store file call KeyFor without a message-only pin: " + string.Join(", ", unpinnedCallers));
    }

    private static bool IsKeyFor(MethodBase target, Type store)
    {
        var declaring = target.DeclaringType;
        var isStoreOrInterface = declaring == store
            || (declaring is { IsGenericType: true } && declaring.GetGenericTypeDefinition() == typeof(ICredentialStore<>));
        return isStoreOrInterface && (target.Name == "KeyFor" || target.Name.EndsWith(".KeyFor", StringComparison.Ordinal));
    }

    // Contract section 4 item 11 and section 5.1: the store is the package's only reader of its section. Any
    // other type handed configuration, or naming a parent section, could reach the credentials, so each one is
    // pinned by the subclass with a reason. A parent-section options binding is safe only while the bound type
    // has nowhere to put an unexpected child section, so its members are checked for catch-alls.
    [Fact]
    [Trait("ContractId", "C31")]
    public void OnlyPinnedTypes_ReadConfigurationOrParentSections()
    {
        var rig = Create();
        var store = rig.Store.GetType();
        var check = CreateStartupCheck(rig.Store, new CapturingLoggerFactory(new CapturingLogger())).GetType();
        var storeFile = new HashSet<string>(StringComparer.Ordinal) { store.FullName!, check.FullName! };

        var readers = new List<(string Type, string Why)>();
        readers.AddRange(ConfigurationConsumerScan.Consumers(store.Assembly).Select(c => (c.Type, c.Member)));
        readers.AddRange(IlScan.Calls(store.Assembly)
            .Where(c => c.Target is MethodInfo method && ConfigurationConsumerScan.Carries(method.ReturnType))
            .Select(c => (ConfigurationConsumerScan.Outermost(c.Type).FullName!, $"{c.Member} calls {c.Target!.DeclaringType?.Name}.{c.Target.Name}")));
        var parentNamers = IlScan.Strings(store.Assembly)
            .Where(found => ContractEntries.IsSegmentParent(found.Value, Prefix))
            .Select(found => (ConfigurationConsumerScan.Outermost(found.Type).FullName!, $"{found.Member} loads \"{found.Value}\""))
            .ToList();
        readers.AddRange(parentNamers);

        // Positive control: the scan sees the store's primary constructor.
        Assert.Contains(readers, r => r.Type == store.FullName);

        var unpinned = readers.Where(r => !storeFile.Contains(r.Type) && !PinnedConfigurationReaders.ContainsKey(r.Type))
            .GroupBy(r => r.Type, StringComparer.Ordinal)
            .Select(g => $"{g.Key} ({string.Join(", ", g.Select(r => r.Why).Distinct())})")
            .ToList();
        Assert.True(unpinned.Count == 0, "Package types that read configuration or name a parent section of the prefix without a pin: " + string.Join("; ", unpinned));

        // A pin with nothing behind it hides the next type that takes the same name.
        var stale = PinnedConfigurationReaders.Keys.Where(pin => !readers.Any(r => r.Type == pin)).ToList();
        Assert.True(stale.Count == 0, "Pinned configuration readers that no longer read configuration: " + string.Join(", ", stale));

        Assert.True(parentNamers.Count == 0 || ParentSectionBoundTypes.Count > 0, "The package names a parent section of the prefix but declares no bound options type: " + string.Join("; ", parentNamers.Select(n => n.Item2)));

        // Positive control: the catch-all check sees each kind of member it exists to stop.
        Assert.Equal(BoundTypeScan.SampleExpected, BoundTypeScan.Samples.Select(t => BoundTypeScan.CatchAllMembers(t).Count).ToArray());
        foreach (var bound in ParentSectionBoundTypes)
        {
            var catchAlls = BoundTypeScan.CatchAllMembers(bound);
            Assert.True(catchAlls.Count == 0, $"{bound.FullName} is bound from a parent section of {Prefix} and has catch-all members that would take a credential: {string.Join(", ", catchAlls)}");
        }
    }

    [Fact]
    [Trait("ContractId", "C19")]
    public void TryGet_UndefinedPart_Refused_NoConfigRead()
    {
        var rig = Create();
        SetAll(rig, "agency", part => $"VALUE-{part}");
        var undefined = UndefinedPart();

        Assert.False(rig.Store.TryGet("agency", undefined, out var value, out var error));
        Assert.Null(value);
        Assert.Equal(UnknownPartError, error);

        // After the guard, before the name check.
        Assert.False(rig.Store.TryGet("a:b", undefined, out _, out var beforeName));
        Assert.Equal(UnknownPartError, beforeName);
        Assert.Empty(rig.Config.Reads);

        var exposed = Create(["Umbraco:Automate"]);
        Assert.False(exposed.Store.TryGet("agency", undefined, out _, out var guardFirst));
        Assert.Equal(GuardError("Umbraco:Automate"), guardFirst);
    }

    private static TPart UndefinedPart()
    {
        long candidate = 1000;
        while (Enum.IsDefined(typeof(TPart), Enum.ToObject(typeof(TPart), candidate)))
        {
            candidate++;
        }

        return (TPart)Enum.ToObject(typeof(TPart), candidate);
    }

    // Red until the contract is frozen: while CanonicalHash is a PENDING placeholder this fails and prints the
    // computed hash, which is how the freeze step obtains it. Never skipped, so a consumer cannot run without it.
    [Fact]
    [Trait("ContractId", "C17")]
    public void Source_MatchesCanonicalHash()
    {
        var files = CredentialHelperSourceHash.ReadAll(typeof(CredentialStoreContractTests<TPart>).Assembly);
        var tokens = CredentialHelperSourceHash.Tokens(Prefix, Namespace, StoreClassName, PartEnumName);
        var perFile = CredentialHelperSourceHash.FileHashes(files, tokens);
        var computed = CredentialHelperSourceHash.Combine(perFile);
        var detail = string.Join("; ", perFile.Select(f => $"{f.Key} {f.Value}"));

        Assert.False(
            CanonicalHash.StartsWith("PENDING", StringComparison.Ordinal),
            $"CanonicalHash is not set yet (credential-helper-contract {ContractVersion} is not frozen). Computed hash: {computed}. Per file: {detail}.");
        Assert.True(
            computed == CanonicalHash,
            $"The credential helper files have diverged from credential-helper-contract {ContractVersion}. Computed hash: {computed}. Per file: {detail}. Re-copy the canonical files or propose the change in the canonical repo; never edit CanonicalHash locally.");
    }

    // The normalisation must give one hash for every way an editor or git can save the same text, and must
    // replace each package value with its token even when one value contains another.
    [Fact]
    [Trait("ContractId", "C17b")]
    public void SourceHash_NormalisationIsStable()
    {
        const string Template = "// marker\nnamespace @@Namespace@@;\n\ninternal sealed class @@Store@@ : I<@@Part@@>\n{\n    public const string Prefix = \"@@Prefix@@\";\n}";
        var tokens = CredentialHelperSourceHash.Tokens("Umbraco:Automate:Acme:Credentials", "Acme.AcmeStore.Credentials", "AcmeStore", "AcmeStorePart");
        var filled = Template
            .Replace("@@Prefix@@", "Umbraco:Automate:Acme:Credentials", StringComparison.Ordinal)
            .Replace("@@Namespace@@", "Acme.AcmeStore.Credentials", StringComparison.Ordinal)
            .Replace("@@Store@@", "AcmeStore", StringComparison.Ordinal)
            .Replace("@@Part@@", "AcmeStorePart", StringComparison.Ordinal);

        var variants = new Dictionary<string, string>
        {
            ["plain"] = filled,
            ["BOM"] = "\uFEFF" + filled,
            ["CRLF"] = filled.Replace("\n", "\r\n"),
            ["lone CR"] = filled.Replace("\n", "\r"),
            ["trailing spaces and tabs"] = filled.Replace("\n", " \t \n") + "\t ",
            ["trailing blank lines"] = filled + "\n\n   \n\t\n",
            ["all at once"] = "\uFEFF" + filled.Replace("\n", " \r\n") + "\r\n\r\n",
        };

        foreach (var (name, text) in variants)
        {
            Assert.True(Template == CredentialHelperSourceHash.Normalise(text, tokens), $"{name} variant normalised to: {CredentialHelperSourceHash.Normalise(text, tokens)}");
        }

        // Only one BOM is dropped, so a second one is content and changes the hash.
        Assert.NotEqual(Template, CredentialHelperSourceHash.Normalise("\uFEFF\uFEFF" + filled, tokens));

        // The suite's own CanonicalHash line is excluded from its hash, so freezing the constant does not change it.
        var suite = "class X\n{\n    public const string CanonicalHash = \"PENDING-X\";\n}";
        Assert.Equal(
            CredentialHelperSourceHash.Normalise(suite, tokens),
            CredentialHelperSourceHash.Normalise(suite.Replace("PENDING-X", new string('a', 64)), tokens));

        // A source that already holds token text would hash the same as one holding the package value, so a
        // store could carry a literal token where the value belongs and still match.
        foreach (var token in CredentialHelperSourceHash.TokenNames)
        {
            Assert.Throws<InvalidOperationException>(() => CredentialHelperSourceHash.Normalise(filled + "\n// " + token, tokens));
        }

        // An empty value would match nothing, and two equal values would map one text to two tokens; either
        // makes the store's hash depend on which token wins, so both are refused when the tokens are built.
        Assert.Throws<ArgumentException>(() => CredentialHelperSourceHash.Tokens("A:B", "Ns", "Store", "Store"));
        Assert.Throws<ArgumentException>(() => CredentialHelperSourceHash.Tokens("A:B", "", "Store", "Part"));
        Assert.Throws<ArgumentException>(() => CredentialHelperSourceHash.Tokens("A:B", "Ns", " ", "Part"));
    }

    [Fact]
    [Trait("ContractId", "C18")]
    public void ContractVersion_IsConsistent()
    {
        foreach (var (resource, text) in CredentialHelperSourceHash.ReadAll(typeof(CredentialStoreContractTests<TPart>).Assembly))
        {
            Assert.True(
                text.Split('\n')[0].TrimEnd('\r') == "// credential-helper-contract: " + ContractVersion,
                $"{resource} does not start with the version marker for {ContractVersion}.");
        }
    }

    // C17 hashes the embedded source with the subclass's declared values swapped for tokens. This binds those
    // declared values to the type the tests actually run, so a subclass cannot embed the canonical file while
    // compiling and testing a different store. Body equivalence of the embedded and compiled source is out of
    // scope (contract section 3).
    [Fact]
    [Trait("ContractId", "C29")]
    public void CompiledStore_MatchesDeclaredTokens()
    {
        var rig = Create();
        var store = rig.Store.GetType();
        var check = CreateStartupCheck(rig.Store, new CapturingLoggerFactory(new CapturingLogger())).GetType();

        Assert.Equal(StoreClassName, store.Name);
        Assert.Equal(Namespace, store.Namespace);
        Assert.Equal(StoreClassName + "StartupCheck", check.Name);
        Assert.Equal(Namespace, check.Namespace);
        Assert.Equal(PartEnumName, typeof(TPart).Name);

        // The package's own assembly, not the test assembly and not one that carries test code.
        Assert.Same(store.Assembly, check.Assembly);
        Assert.NotSame(typeof(CredentialStoreContractTests<TPart>).Assembly, store.Assembly);
        var testReferences = store.Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Moq", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(testReferences.Count == 0, $"The store's assembly references test frameworks: {string.Join(", ", testReferences)}");

        var prefix = store.GetField("Prefix", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.True(prefix is { IsLiteral: true } && prefix.FieldType == typeof(string), $"{store.FullName} has no public const string Prefix.");
        Assert.Equal(Prefix, (string?)prefix.GetRawConstantValue());
    }

    // A reviewer reads the hashed files as text. A look-alike letter, a zero-width character or a stray control
    // character changes behaviour without being visible in a diff, so the files hold printable ASCII only and
    // non-ASCII test inputs are written as \u escapes (contract section 3).
    [Fact]
    [Trait("ContractId", "C30")]
    public void HashedFiles_HoldPrintableAsciiOnly()
    {
        // Positive controls: the check sees each kind of character it exists to stop, and allows what git and
        // editors legitimately write (one leading BOM, CRLF line endings, tabs).
        var flagged = new Dictionary<string, byte[]>
        {
            ["e-acute"] = Encoding.UTF8.GetBytes("ag\u00E9ncy"),
            ["Cyrillic a"] = Encoding.UTF8.GetBytes("\u0430gency"),
            ["zero-width space"] = Encoding.UTF8.GetBytes("agency\u200B"),
            ["second BOM"] = Encoding.UTF8.GetBytes("\uFEFF\uFEFFx"),
            ["BOM mid-file"] = Encoding.UTF8.GetBytes("x\uFEFF"),
            ["DEL"] = [0x78, 0x7F],
            ["NUL"] = [0x78, 0x00],
            ["form feed"] = [0x78, 0x0C],
            ["lone CR"] = [0x78, 0x0D, 0x78],
        };
        foreach (var (name, bytes) in flagged)
        {
            Assert.True(CredentialHelperSourceHash.NonPrintableAscii(bytes).Count > 0, $"C30 does not flag a {name}.");
        }

        Assert.Empty(CredentialHelperSourceHash.NonPrintableAscii(Encoding.UTF8.GetBytes("\uFEFFline\r\n\tline\n~")));

        foreach (var resource in CredentialHelperSourceHash.Resources)
        {
            var offences = CredentialHelperSourceHash.NonPrintableAscii(CredentialHelperSourceHash.ReadBytes(typeof(CredentialStoreContractTests<TPart>).Assembly, resource));
            Assert.True(offences.Count == 0, $"{resource} holds raw non-ASCII or control characters; write them as \\u escapes: {string.Join(", ", offences)}");
        }
    }
}

/// <summary>Allow-list entries computed from a prefix, so every package runs the same corpus.</summary>
internal static class ContractEntries
{
    public static IEnumerable<string> Parents(string prefix)
    {
        var segments = prefix.Split(':');
        for (var length = 1; length < segments.Length; length++)
        {
            yield return string.Join(':', segments.Take(length));
        }
    }

    /// <summary>
    /// True when <paramref name="literal"/>, normalised as the guard normalises an entry (contract section 1.4),
    /// names a strict parent section of <paramref name="prefix"/>: a configuration read of it returns the
    /// credential section as a child (C31).
    /// </summary>
    public static bool IsSegmentParent(string literal, string prefix)
    {
        var segments = literal.Trim().Replace("__", ":").Trim(':').Split(':', StringSplitOptions.RemoveEmptyEntries);
        var prefixSegments = prefix.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0
            && segments.Length < prefixSegments.Length
            && segments.Select((s, i) => string.Equals(s, prefixSegments[i], StringComparison.OrdinalIgnoreCase)).All(equal => equal);
    }

    // Every spelling of an entry that exposes the prefix (section 5.1, C10).
    public static IEnumerable<string> Exposing(string prefix)
    {
        foreach (var parent in Parents(prefix))
        {
            yield return parent;
        }

        yield return prefix;
        yield return " " + prefix.ToUpperInvariant() + ": ";
        yield return prefix + ":agency";
        yield return prefix.Replace(":", "__") + "__agency";
        yield return string.Empty;
        yield return "   ";
        yield return "Umbraco:*";
        yield return prefix.Split(':')[0] + ":?";
        yield return ":";
        yield return "__";
    }

    // Entries that share characters but not segments with the prefix (section 5.1, C11).
    public static IEnumerable<string> NotExposing(string prefix)
    {
        yield return "Umbraco:Automate:Secrets";
        yield return "Umbraco:Automate:Variables";
        yield return prefix + "X";
        foreach (var parent in Parents(prefix))
        {
            yield return parent + "X";
        }
    }
}

/// <summary>
/// The divergence hash (contract section 3). Kept apart from the test class so its normalisation is tested
/// directly (C17b) rather than only through the hash of the real files.
/// </summary>
internal static class CredentialHelperSourceHash
{
    public const string StoreResource = "CredentialHelperContract.StoreSource.cs";
    public const string StoreSuiteResource = "CredentialHelperContract.StoreContractTests.cs";
    public const string IsolationSuiteResource = "CredentialHelperContract.IsolationContractTests.cs";

    // The suite cannot hash its own CanonicalHash value, so that one line is replaced before hashing.
    private static readonly Regex CanonicalHashLine = new(@"^(?<indent>[ \t]*)public const string CanonicalHash = ""[^""\n]*"";$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Package values mapped to their contract tokens. Only the store carries them; the suites are neutral. The
    /// tokens are written @@Name@@ because that text cannot occur in C# source, so no real code is ever mistaken
    /// for a token.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Tokens(string prefix, string ns, string store, string part)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (value, token) in new[] { (prefix, TokenNames[0]), (ns, TokenNames[1]), (store, TokenNames[2]), (part, TokenNames[3]) })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"The value for {token} is empty.");
            }

            if (!tokens.TryAdd(value, token))
            {
                throw new ArgumentException($"The value '{value}' is given for both {tokens[value]} and {token}.");
            }
        }

        return tokens;
    }

    public static IReadOnlyList<string> TokenNames { get; } = ["@@Prefix@@", "@@Namespace@@", "@@Store@@", "@@Part@@"];

    public static IReadOnlyList<string> Resources { get; } = [StoreResource, StoreSuiteResource, IsolationSuiteResource];

    public static IReadOnlyList<KeyValuePair<string, string>> ReadAll(Assembly assembly) =>
        Resources.Select(name => KeyValuePair.Create(name, Read(assembly, name))).ToList();

    public static byte[] ReadBytes(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource {resource} is missing (credential-helper-contract section 4 item 6).");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Contract section 3 (C30): every byte after one optional leading UTF-8 BOM must be printable ASCII
    /// (0x20 to 0x7E), a tab or a line feed. A carriage return is allowed only before a line feed, because a
    /// Windows checkout writes CRLF and the hash already normalises it; a lone CR is reported.
    /// </summary>
    public static IReadOnlyList<string> NonPrintableAscii(byte[] bytes)
    {
        var offences = new List<string>();
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        var line = 1;
        for (var i = start; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if (b == 0x0A)
            {
                line++;
                continue;
            }

            var allowed = b is 0x09 or (>= 0x20 and <= 0x7E) || (b == 0x0D && i + 1 < bytes.Length && bytes[i + 1] == 0x0A);
            if (!allowed)
            {
                offences.Add($"line {line} byte 0x{b:X2}");
            }
        }

        return offences;
    }

    public static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource {resource} is missing (credential-helper-contract section 4 item 6).");

        // No BOM detection: Normalise drops exactly one BOM, so a reader that silently ate any number of them
        // would hide a difference.
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    /// <summary>Contract section 3 step 2: line endings, trailing white space, one BOM, the hash line, then tokens.</summary>
    public static string Normalise(string raw, IReadOnlyDictionary<string, string> tokens)
    {
        var text = raw.StartsWith('\uFEFF') ? raw[1..] : raw;
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = string.Join('\n', text.Split('\n').Select(line => line.TrimEnd(' ', '\t'))).TrimEnd('\n');
        text = CanonicalHashLine.Replace(text, "${indent}public const string CanonicalHash = \"<excluded>\";");

        // A source that already held token text would hash like one that held the package value.
        if (tokens.Count > 0 && TokenNames.FirstOrDefault(token => text.Contains(token, StringComparison.Ordinal)) is { } written)
        {
            throw new InvalidOperationException($"The store source already contains the token text {written}; it must hold the package's own values.");
        }

        // One pass, longest value first: a value that contains another (a Part name that contains the Store
        // name) is replaced whole, and a token already written is never matched again.
        var values = tokens.Keys.Where(v => v.Length > 0).OrderByDescending(v => v.Length).Select(Regex.Escape).ToList();
        return values.Count == 0
            ? text
            : Regex.Replace(text, string.Join('|', values), match => tokens[match.Value], RegexOptions.CultureInvariant);
    }

    /// <summary>Per-file SHA-256; the suites are package-neutral, so only the store has tokens replaced.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> FileHashes(IReadOnlyList<KeyValuePair<string, string>> files, IReadOnlyDictionary<string, string> tokens) =>
        files.Select(file => KeyValuePair.Create(
                file.Key,
                Sha256(Normalise(file.Value, file.Key == StoreResource ? tokens : new Dictionary<string, string>()))))
            .ToList();

    /// <summary>Contract section 3 step 4: SHA-256 of the per-file hashes, one "resource hash" line each, in file order.</summary>
    public static string Combine(IReadOnlyList<KeyValuePair<string, string>> fileHashes) =>
        Sha256(string.Join('\n', fileHashes.Select(f => $"{f.Key} {f.Value}")));

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>
/// Finds members of an options type that could take a configuration child the type does not name (C31). The
/// binder fills a dictionary, a configuration section, a JSON element or an object-typed member with whatever
/// children the bound section holds, so a type bound from a parent of the credential section with such a member
/// would copy the credentials into an options object.
/// </summary>
internal static class BoundTypeScan
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Positive controls for the scan: how many catch-alls each sample holds.
    public static readonly Type[] Samples = [typeof(CleanSample), typeof(DictionarySample), typeof(NestedSample), typeof(ListSample)];
    public static readonly int[] SampleExpected = [0, 4, 5, 1];

    public static IReadOnlyList<string> CatchAllMembers(Type type)
    {
        var found = new List<string>();
        Walk(type, type.Name, found, []);
        return found;
    }

    private static void Walk(Type type, string path, List<string> found, HashSet<Type> seen)
    {
        if (!seen.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(Instance).Where(p => p.GetIndexParameters().Length == 0))
        {
            Check(property.PropertyType, $"{path}.{property.Name}", found, seen);
        }
    }

    private static void Check(Type type, string path, List<string> found, HashSet<Type> seen)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsCatchAll(type))
        {
            found.Add($"{path} ({type.Name})");
            return;
        }

        if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type.IsPointer)
        {
            return;
        }

        var element = type.IsArray
            ? type.GetElementType()
            : type.GetInterfaces().Append(type)
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
        if (element is not null)
        {
            Check(element, path + "[]", found, seen);
            return;
        }

        // Framework value types (TimeSpan, Uri, DateTime and so on) bind from one string, not from children.
        if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true)
        {
            Walk(type, path, found, seen);
        }
    }

    private static bool IsCatchAll(Type type) =>
        type == typeof(object)
        || typeof(IConfiguration).IsAssignableFrom(type)
        || typeof(System.Collections.IDictionary).IsAssignableFrom(type)
        || type.GetInterfaces().Append(type).Any(i => i.IsGenericType
            && (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
        || type.FullName is "System.Text.Json.JsonElement" or "System.Dynamic.ExpandoObject"
        || type.FullName?.StartsWith("System.Text.Json.Nodes.", StringComparison.Ordinal) == true;

    private sealed class CleanSample
    {
        public int Number { get; set; }

        public string? Text { get; set; }

        public TimeSpan Span { get; set; }

        public List<string> Names { get; set; } = [];
    }

    private sealed class DictionarySample
    {
        public Dictionary<string, string> Map { get; set; } = [];

        public IConfigurationSection? Section { get; set; }

        public System.Text.Json.JsonElement Element { get; set; }

        public object? Bag { get; set; }
    }

    private sealed class NestedSample
    {
        public DictionarySample? Inner { get; set; }

        public IReadOnlyDictionary<string, int>? ReadOnlyMap { get; set; }
    }

    private sealed class ListSample
    {
        public List<object> Items { get; set; } = [];
    }
}

/// <summary>
/// The string literals an assembly's IL loads and the methods it calls, each with the type and member that does
/// it. Lambdas and async state machines are reported against the type whose code they came from.
/// </summary>
internal static class IlScan
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => op.Value);

    public static IEnumerable<(Type Type, string Member, string Value)> Strings(Assembly assembly) =>
        from found in Instructions(assembly)
        where found.OpCode == OpCodes.Ldstr
        select (found.Type, found.Method.Name, found.Method.Module.ResolveString(found.Operand));

    /// <summary>
    /// Every call, virtual call, constructor call and delegate creation, with the target resolved; a target the
    /// module cannot resolve is reported as null so a caller can fail closed rather than miss it.
    /// </summary>
    public static IEnumerable<(Type Type, string Member, MethodBase? Target)> Calls(Assembly assembly) =>
        from found in Instructions(assembly)
        where found.OpCode == OpCodes.Call || found.OpCode == OpCodes.Callvirt || found.OpCode == OpCodes.Newobj
            || found.OpCode == OpCodes.Ldftn || found.OpCode == OpCodes.Ldvirtftn
        select (found.Type, found.Method.Name, Resolve(found.Method, found.Operand));

    private static MethodBase? Resolve(MethodBase caller, int token)
    {
        try
        {
            return caller.Module.ResolveMethod(
                token,
                caller.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null,
                caller.IsGenericMethod ? caller.GetGenericArguments() : null);
        }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or TypeLoadException or FileNotFoundException)
        {
            return null;
        }
    }

    private static IEnumerable<(Type Type, MethodBase Method, OpCode OpCode, int Operand)> Instructions(Assembly assembly)
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetTypes())
        {
            var outer = type;
            while (outer.DeclaringType is not null && outer.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            {
                outer = outer.DeclaringType;
            }

            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                foreach (var (opCode, operand) in Walk(method))
                {
                    yield return (outer, method, opCode, operand);
                }
            }
        }
    }

    // Walks the opcodes properly rather than searching for an opcode byte, so an operand byte is never misread.
    // The operand is returned for 4-byte token operands only, which is all the callers need.
    private static IEnumerable<(OpCode OpCode, int Operand)> Walk(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE)
            {
                value = (short)(0xFE00 | il[position++]);
            }

            var opCode = OpCodesByValue[value];
            var size = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
                _ => 4,
            };
            yield return (opCode, size == 4 ? BitConverter.ToInt32(il, position) : 0);
            position += size;
        }
    }
}

/// <summary>Reloadable in-memory configuration that records every key read through it.</summary>
internal sealed class RecordingConfiguration : IConfigurationRoot
{
    private readonly MutableProvider provider = new();
    private readonly ConfigurationRoot root;
    private readonly List<string> reads = [];

    public RecordingConfiguration() => root = new ConfigurationRoot([provider]);

    public IReadOnlyList<string> Reads
    {
        get
        {
            lock (reads)
            {
                return reads.ToArray();
            }
        }
    }

    public IEnumerable<IConfigurationProvider> Providers => root.Providers;

    public string? this[string key]
    {
        get
        {
            Record(key);
            return root[key];
        }
        set => root[key] = value;
    }

    public void Set(string key, string? value) => provider.SetValue(key, value);

    public void Reload()
    {
        provider.Touch();
        root.Reload();
    }

    public IConfigurationSection GetSection(string key)
    {
        Record(key);
        return root.GetSection(key);
    }

    public IEnumerable<IConfigurationSection> GetChildren()
    {
        Record("<children>");
        return root.GetChildren();
    }

    public IChangeToken GetReloadToken() => root.GetReloadToken();

    private void Record(string key)
    {
        lock (reads)
        {
            reads.Add(key);
        }
    }

    private sealed class MutableProvider : ConfigurationProvider
    {
        public void SetValue(string key, string? value) => Data[key] = value;

        public void Touch() => OnReload();
    }
}

/// <summary>An options monitor whose value can be replaced; replacing it raises OnChange.</summary>
internal sealed class TestOptionsMonitor<T>(T initial) : IOptionsMonitor<T>
    where T : class
{
    private readonly List<Action<T, string?>> listeners = [];
    private T current = initial;

    public T CurrentValue
    {
        get => current;
        set
        {
            current = value;
            foreach (var listener in listeners.ToArray())
            {
                listener(value, null);
            }
        }
    }

    public T Get(string? name) => current;

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        listeners.Add(listener);
        return new Subscription(() => listeners.Remove(listener));
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

internal sealed record CapturedLog(LogLevel Level, string Message, string Text);

/// <summary>Captures every log call at Trace and above, with the exception text if any.</summary>
internal sealed class CapturingLogger : ILogger
{
    private readonly List<CapturedLog> entries = [];

    public IReadOnlyList<CapturedLog> Entries
    {
        get
        {
            lock (entries)
            {
                return entries.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        lock (entries)
        {
            entries.Add(new CapturedLog(logLevel, message, message + exception));
        }
    }
}

internal sealed class CapturingLoggerFactory(CapturingLogger logger) : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => logger;

    public void Dispose()
    {
    }
}
