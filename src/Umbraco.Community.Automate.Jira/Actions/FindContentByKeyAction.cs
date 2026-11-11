using System.Globalization;
using System.Text.RegularExpressions;
using Examine;
using Examine.Search;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Security;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Actions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.Automate.Jira.Connection;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>
/// Finds the one content node whose Jira-key property equals a key (design 6.7, F-01). Exists because the built-in
/// Find Content matches node names, which any editor can change. Reads only: not an <c>ICmsAction</c>.
/// </summary>
/// <remarks>
/// One Examine query and one batch load (pitfalls P3, P4, P9). The index only proposes candidates; each one is
/// checked against its draft property value, and the count is taken before permissions so a duplicate the
/// service account cannot see still stops the run.
/// </remarks>
[Action(
    "community.jira.findContentByKey",
    "Find content by Jira key",
    Description = "Finds the one content node whose Jira key property equals the issue key. Fails if none or more than one match.",
    Group = "Content",
    Icon = "icon-search",
    RequiredSections = [Constants.Applications.Content],
    RequiredPermissions = [ActionBrowse.ActionLetter])]
public sealed partial class FindContentByKeyAction : ActionBase<FindContentByKeySettings, FindContentByKeyOutput>
{
    /// <summary>Candidates read from the index; this many or more fails the step.</summary>
    public const int MaxCandidates = 10;

    private readonly IExamineManager _examine;
    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly IAutomationActionAuthorizer _authorizer;

    /// <summary>Creates the action. Same dependencies as Automate's built-in Find Content, all site singletons.</summary>
    public FindContentByKeyAction(
        ActionInfrastructure infrastructure,
        IExamineManager examine,
        IContentService contentService,
        IContentTypeService contentTypeService,
        IAutomationActionAuthorizer authorizer)
        : base(infrastructure)
    {
        _examine = examine;
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _authorizer = authorizer;
    }

    /// <inheritdoc />
    public override async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        if (context.GetSettings<FindContentByKeySettings>() is not { } settings)
        {
            return ActionFailures.Validation("The step has no settings.");
        }

        // Numeric issue IDs are refused: the property stores keys. The key rule also stops query injection.
        if (!JiraIdentifiers.TryIssueKey(settings.IssueKey, out var issueKey, out var error))
        {
            return ActionFailures.Validation("Issue key: " + error);
        }

        var alias = settings.PropertyAlias?.Trim() ?? string.Empty;
        if (!AliasRule().IsMatch(alias))
        {
            return ActionFailures.Validation("Property alias must start with a letter and use only letters, digits and underscores (up to 64).");
        }

        if (!TryResolveTypes(settings.ContentTypes, ref alias, out var types, out var failure))
        {
            return failure;
        }

        if (!_examine.TryGetIndex(Constants.UmbracoIndexes.InternalIndexName, out var index))
        {
            return ActionFailures.Configuration("The InternalIndex is not available on this site.");
        }

        // InternalIndex includes drafts: a page waiting to go live is often unpublished. The phrase is lower-cased
        // to match the index's analysis; every value in it has passed a strict rule, so nothing needs escaping.
        var typeClause = string.Join(' ', types.Select(t => "__NodeTypeAlias:" + t.Alias));
        var query = $"+__IndexType:content +{alias}:\"{issueKey!.ToLowerInvariant()}\" +({typeClause})";
        var results = index.Searcher.CreateQuery().NativeQuery(query).Execute(QueryOptions.SkipTake(0, MaxCandidates));
        if (results.TotalItemCount >= MaxCandidates)
        {
            return ActionFailures.Validation($"Too many candidates for {issueKey} (at least {MaxCandidates}); check that {alias} values are unique.");
        }

        var ids = results.Select(r => int.Parse(r.Id, NumberStyles.Integer, CultureInfo.InvariantCulture)).ToArray();
        var typeKeys = types.Select(t => t.Key).ToHashSet();

        // Text fields are tokenised, so the index can over-match (SUP-17 inside "SUP-17 old"). Only the exact draft
        // value counts.
        var matches = ids.Length == 0
            ? []
            : _contentService.GetByIds(ids)
                .Where(c => !c.Trashed && typeKeys.Contains(c.ContentType.Key))
                .Where(c => c.GetValue(alias) is string value && value.AsSpan().Trim(AsciiWhitespace).Equals(issueKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var typeNames = string.Join(", ", types.Select(t => t.Alias));
        if (matches.Count == 0)
        {
            return ActionFailures.Validation($"No {typeNames} content has {alias} = {issueKey}.");
        }

        // Counted before permissions (design 6.7 step 4), and no content key is returned.
        if (matches.Count > 1)
        {
            return ActionFailures.Validation($"{matches.Count} nodes have {alias} = {issueKey}: {string.Join(", ", matches.Select(m => m.Key))}. Exactly one may hold it.");
        }

        var match = matches[0];
        // Null means authorised (Automate's "OrFail" helpers return a failure only); same check as Publish Content.
        var denied = await _authorizer.AuthorizeContentOrFailAsync(match.Key, Permissions(), cancellationToken);
        if (denied is { Status: not ActionResultStatus.Success })
        {
            return denied;
        }

        context.LogInfo($"Found {issueKey} on node {match.Key}.");
        return Success(new FindContentByKeyOutput
        {
            ContentKey = match.Key,
            Name = match.Name ?? string.Empty,
            ContentTypeAlias = match.ContentType.Alias,
            IsPublished = match.Published,
            UpdateDate = match.UpdateDate,
        });
    }

    private static IReadOnlyList<string> Permissions() => [ActionBrowse.ActionLetter];

    // SEC-M3-6: only the whitespace the index's tokenizer also splits on, so a value never counts as a holder here
    // while the index could not have offered it (a non-breaking space stays part of the token).
    private const string AsciiWhitespace = " \t\r\n";

    // Resolves the types and replaces alias with the property's own alias: Umbraco matches aliases ignoring case,
    // but Lucene field names are case-sensitive (QA-M3-5).
    private bool TryResolveTypes(string? csv, ref string alias, out List<IContentType> types, out ActionResult failure)
    {
        types = [];
        failure = null!;
        var entries = (csv ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
        {
            failure = ActionFailures.Validation("Choose at least one content type to search.");
            return false;
        }

        foreach (var entry in entries)
        {
            // Fail closed on a stale key: the built-in drops it and then searches every type.
            if (!Guid.TryParse(entry, out var key) || _contentTypeService.Get(key) is not { } type)
            {
                failure = ActionFailures.Configuration($"Content type {entry} no longer exists. Update the step's content types.");
                return false;
            }

            if (!AliasRule().IsMatch(type.Alias))
            {
                failure = ActionFailures.Configuration($"Content type alias {type.Alias} cannot be searched safely.");
                return false;
            }

            var wanted = alias;
            var property = type.CompositionPropertyTypes.FirstOrDefault(p => string.Equals(p.Alias, wanted, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                failure = ActionFailures.Configuration($"Content type {type.Alias} has no property {alias}.");
                return false;
            }

            if (types.Count > 0 && !string.Equals(property.Alias, alias, StringComparison.Ordinal))
            {
                failure = ActionFailures.Configuration($"The property alias differs in case between the chosen content types ({alias}, {property.Alias}).");
                return false;
            }

            alias = property.Alias;

            if (property.Variations != ContentVariation.Nothing)
            {
                failure = ActionFailures.Configuration($"The Jira key property {alias} on {type.Alias} must be invariant.");
                return false;
            }

            types.Add(type);
        }

        return true;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex AliasRule();
}
