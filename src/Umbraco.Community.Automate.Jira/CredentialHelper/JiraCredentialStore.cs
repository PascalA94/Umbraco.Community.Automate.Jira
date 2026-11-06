// credential-helper-contract: 1.0.0
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Automate.Core.Configuration;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.Community.Automate.Jira.Credentials;

// Copied verbatim into every package that uses the credential helper (credential-helper-contract.md).
// Never edit a copy in place: change the contract in its canonical repo first, then re-copy.
internal interface ICredentialStore<TPart>
    where TPart : struct, Enum
{
    bool TryNormaliseName(string? name, out string? normalised, out string? error);

    bool MatchesNameRule(string candidate);

    bool TryGetExposingPrefix(out string? entry);

    IReadOnlyList<string> GetExposingPrefixes();

    bool TryGet(string? name, TPart part, out string? value, out string? error);

    string KeyFor(string normalisedName, TPart part);
}

// Credentials live under a config prefix that Automate's reference resolver refuses to
// read, so no automation step can turn a reference into a value. The guard fails closed
// if a site widens Automate's allow-list to cover this prefix.
//
// Two options sources are checked because they can disagree: the core resolver reads its
// allow-list once at startup (the IOptions snapshot), while IOptionsMonitor follows live
// changes. An entry in either one means the prefix is treated as exposed.
internal sealed partial class JiraCredentialStore(
    IConfiguration configuration,
    IOptionsMonitor<AutomateOptions> automateOptions,
    IOptions<AutomateOptions> automateStartupOptions)
    : ICredentialStore<JiraCredentialPart>
{
    public const string Prefix = "Umbraco:Automate:Jira:Credentials";

    public bool TryNormaliseName(string? name, out string? normalised, out string? error)
    {
        normalised = null;

        // Never echo the input: a user may have pasted a secret into the name field.
        error = "Credential name must be 1 to 32 letters or digits.";
        if (name is null)
        {
            return false;
        }

        var trimmed = name.Trim();

        // Reject non-ASCII before lower-casing: the Kelvin sign lower-cases to ASCII 'k',
        // and zero-width characters survive Trim.
        foreach (var c in trimmed)
        {
            if (c > 0x7F)
            {
                return false;
            }
        }

        var lowered = trimmed.ToLowerInvariant();
        if (!MatchesNameRule(lowered))
        {
            return false;
        }

        normalised = lowered;
        error = null;
        return true;
    }

    public bool MatchesNameRule(string candidate) => NameRule().IsMatch(candidate);

    public bool TryGetExposingPrefix(out string? entry)
    {
        var exposing = GetExposingPrefixes();
        entry = exposing.Count > 0 ? exposing[0] : null;
        return exposing.Count > 0;
    }

    public IReadOnlyList<string> GetExposingPrefixes()
    {
        // Read on every call so a runtime options change trips the guard without a restart.
        var exposing = new List<string>();
        AddExposing(automateOptions.CurrentValue, exposing);
        AddExposing(automateStartupOptions.Value, exposing);
        return exposing;
    }

    public bool TryGet(string? name, JiraCredentialPart part, out string? value, out string? error)
    {
        value = null;

        if (TryGetExposingPrefix(out var entry))
        {
            error = $"Refused: Umbraco:Automate:AllowedConfigurationKeyPrefixes contains '{entry}', which exposes {Prefix}. Remove that entry.";
            return false;
        }

        // An out-of-range enum value would build a key for a part that does not exist.
        if (!Enum.IsDefined(part))
        {
            error = "Unknown credential part.";
            return false;
        }

        if (!TryNormaliseName(name, out var normalised, out error))
        {
            return false;
        }

        var key = KeyFor(normalised!, part);

        // Never cached: a reloading provider rotates the value without a restart.
        var configured = configuration[key];
        if (string.IsNullOrWhiteSpace(configured))
        {
            error = $"No value is configured at {key}.";
            return false;
        }

        value = configured;
        error = null;
        return true;
    }

    // For messages only; never index configuration with it outside TryGet, which runs the guard and the name
    // rule first. A second reader would bypass both.
    public string KeyFor(string normalisedName, JiraCredentialPart part) => $"{Prefix}:{normalisedName}:{part}";

    private static void AddExposing(AutomateOptions options, List<string> exposing)
    {
        var entries = options.AllowedConfigurationKeyPrefixes;
        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            var text = entry ?? string.Empty;

            // Both sources often hold the same entry; report it once.
            if (IsExposing(entry) && !exposing.Contains(text, StringComparer.Ordinal))
            {
                exposing.Add(text);
            }
        }
    }

    private static bool IsExposing(string? entry)
    {
        // Empty and wildcard entries fail closed: the core's meaning of either is not pinned.
        if (string.IsNullOrWhiteSpace(entry) || entry.Contains('*') || entry.Contains('?'))
        {
            return true;
        }

        var entrySegments = entry.Trim().Replace("__", ":").Trim(':').Split(':', StringSplitOptions.RemoveEmptyEntries);
        var prefixSegments = Prefix.Split(':', StringSplitOptions.RemoveEmptyEntries);

        // Exposing when one segment list is a prefix of the other: a parent, Prefix itself or a child.
        var shared = Math.Min(entrySegments.Length, prefixSegments.Length);
        for (var i = 0; i < shared; i++)
        {
            if (!string.Equals(entrySegments[i], prefixSegments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    // \z, not $: $ also matches before a final newline.
    [GeneratedRegex(@"^[a-z0-9]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NameRule();
}

internal sealed class JiraCredentialStoreStartupCheck(JiraCredentialStore store, ILogger<JiraCredentialStoreStartupCheck> logger)
    : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification)
    {
        // Log only: never throw, stop the site, read a credential or touch the network.
        foreach (var entry in store.GetExposingPrefixes())
        {
            logger.LogCritical(
                "Refused: Umbraco:Automate:AllowedConfigurationKeyPrefixes contains '{Entry}', which exposes {Prefix}. Remove that entry.",
                entry,
                JiraCredentialStore.Prefix);
        }
    }
}
