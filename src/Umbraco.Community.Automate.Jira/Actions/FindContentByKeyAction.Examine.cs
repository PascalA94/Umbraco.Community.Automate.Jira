#if !AUTOMATE_19
using System.Globalization;
using Examine;
using Examine.Search;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Security;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.Automate.Jira.Actions;

// Lines 17 and 18: one Examine query on the InternalIndex and one batch load (pitfalls P3, P4, P9).
public sealed partial class FindContentByKeyAction
{
    /// <summary>Candidates read from the index; this many or more fails the step.</summary>
    public const int MaxCandidates = 10;

    private const string ActionDescription = BaseDescription;

    private readonly IExamineManager _examine;

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

    private Task<(IReadOnlyList<IContent> Candidates, ActionResult? Failure)> FindCandidatesAsync(
        IReadOnlyList<IContentType> types, string alias, string issueKey, CancellationToken cancellationToken)
    {
        if (!_examine.TryGetIndex(Constants.UmbracoIndexes.InternalIndexName, out var index))
        {
            return Task.FromResult<(IReadOnlyList<IContent>, ActionResult?)>(
                ([], ActionFailures.Configuration("The InternalIndex is not available on this site.")));
        }

        // InternalIndex includes drafts: a page waiting to go live is often unpublished. The phrase is lower-cased
        // to match the index's analysis; every value in it has passed a strict rule, so nothing needs escaping.
        var typeClause = string.Join(' ', types.Select(t => "__NodeTypeAlias:" + t.Alias));
        var query = $"+__IndexType:content +{alias}:\"{issueKey.ToLowerInvariant()}\" +({typeClause})";
        var results = index.Searcher.CreateQuery().NativeQuery(query).Execute(QueryOptions.SkipTake(0, MaxCandidates));
        if (results.TotalItemCount >= MaxCandidates)
        {
            return Task.FromResult<(IReadOnlyList<IContent>, ActionResult?)>(
                ([], ActionFailures.Validation($"Too many candidates for {issueKey} (at least {MaxCandidates}); check that {alias} values are unique.")));
        }

        var ids = results.Select(r => int.Parse(r.Id, NumberStyles.Integer, CultureInfo.InvariantCulture)).ToArray();
        IReadOnlyList<IContent> candidates = ids.Length == 0 ? [] : _contentService.GetByIds(ids).ToList();
        return Task.FromResult<(IReadOnlyList<IContent>, ActionResult?)>((candidates, null));
    }
}
#endif
