using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Connection;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>The result of matching a requested transition against the ones Jira offers.</summary>
internal abstract record TransitionMatch
{
    private TransitionMatch()
    {
    }

    /// <summary>Exactly one transition matched.</summary>
    public sealed record Matched(JiraTransition Transition) : TransitionMatch;

    /// <summary>More than one transition matched at the first level that matched anything.</summary>
    public sealed record Ambiguous(IReadOnlyList<JiraTransition> Candidates) : TransitionMatch;

    /// <summary>Nothing matched.</summary>
    public sealed record None : TransitionMatch;
}

/// <summary>
/// Matches what an editor typed to a transition (design 6.4): exact ID, then transition name, then target status
/// name, case-insensitive. Editors usually think "move to Done", a status, while the transition may be called
/// "Close". The first level with any match decides; two matches at that level are ambiguous rather than guessed.
/// </summary>
internal static class TransitionMatcher
{
    public static TransitionMatch Match(IReadOnlyList<JiraTransition> available, string requested)
    {
        var wanted = requested.Trim();

        // A non-numeric value is only ever a name (F-09), so "../5" can never be read as an ID.
        IEnumerable<IReadOnlyList<JiraTransition>> levels =
        [
            JiraIdentifiers.IsNumericId(wanted) ? [.. available.Where(t => string.Equals(t.Id, wanted, StringComparison.Ordinal))] : [],
            [.. available.Where(t => string.Equals(t.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))],
            [.. available.Where(t => string.Equals(t.ToStatusName.Trim(), wanted, StringComparison.OrdinalIgnoreCase))],
        ];

        foreach (var level in levels)
        {
            switch (level.Count)
            {
                case 1:
                    return new TransitionMatch.Matched(level[0]);
                case > 1:
                    return new TransitionMatch.Ambiguous(level);
            }
        }

        return new TransitionMatch.None();
    }
}
