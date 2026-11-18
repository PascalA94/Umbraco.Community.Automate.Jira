#if AUTOMATE_19
using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Security;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Extensions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Search;
using Umbraco.Cms.Core.Search.Querying;
using Umbraco.Cms.Core.Search.Querying.Filtering;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.Automate.Jira.Actions;

// Line 19: Examine is gone from the CMS surface, so candidates come from Umbraco Search's draft content index.
// A stock Textstring is indexed as analysed full text only, so the search can only narrow the field: it over-matches
// by prefix (SUP-17 also offers SUP-170), and the exact recheck in ExecuteAsync decides. That is also why the
// 17/18 ten-candidate limit is gone here: a short key legitimately proposes many nodes, and the duplicate rule on
// the rechecked matches is what protects uniqueness.
public sealed partial class FindContentByKeyAction
{
    /// <summary>Search hits examined before the step fails closed.</summary>
    public const int MaxHitsExamined = 2000;

    /// <summary>Hits read per search call.</summary>
    public const int PageSize = 100;

    private const string ActionDescription = BaseDescription + " After upgrading to Umbraco 19, rebuild the Umbraco Search indexes first.";

    private readonly ISearcherResolver _searchers;
    private readonly ILanguageService _languages;

    /// <summary>Creates the action. Same dependencies as Automate's built-in Find Content on 19, all site singletons.</summary>
    /// <param name="infrastructure">Automate's shared action services.</param>
    /// <param name="searchers">Resolves the Umbraco Search searcher for the draft content index.</param>
    /// <param name="languages">Gives the default language, which the search needs as its culture.</param>
    /// <param name="contentService">Loads each candidate's draft value for the exact recheck.</param>
    /// <param name="contentTypeService">Resolves the chosen content types.</param>
    /// <param name="authorizer">Checks the service account may browse the match.</param>
    public FindContentByKeyAction(
        ActionInfrastructure infrastructure,
        ISearcherResolver searchers,
        ILanguageService languages,
        IContentService contentService,
        IContentTypeService contentTypeService,
        IAutomationActionAuthorizer authorizer)
        : base(infrastructure)
    {
        _searchers = searchers;
        _languages = languages;
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _authorizer = authorizer;
    }

    private async Task<(IReadOnlyList<IContent> Candidates, ActionResult? Failure)> FindCandidatesAsync(
        IReadOnlyList<IContentType> types, string alias, string issueKey, CancellationToken cancellationToken)
    {
        // The draft index includes unpublished pages (a page waiting to go live often is) and trashed ones.
        var searcher = _searchers.GetSearcher(Constants.Search.IndexAliases.DraftContent);
        if (searcher is null)
        {
            return ([], ActionFailures.Configuration("No searcher is registered for the Umbraco Search draft content index."));
        }

        // Culture-variant documents have no invariant entry, so search in the default language, as Automate's
        // Find Content does.
        var culture = (await _languages.GetAllAsync()).FirstOrDefault(l => l.IsDefault)?.IsoCode;

        var filters = new List<Filter>
        {
            new KeywordFilter(Constants.Search.FieldNames.ContentTypeId, types.Select(t => t.Key.AsKeyword()).ToArray(), false),
            // Negated: not in the recycle bin. The Trashed recheck stays as the second guard.
            new KeywordFilter(Constants.Search.FieldNames.PathIds, [Constants.System.RecycleBinContentKey.AsKeyword()], true),
        };

        // Values inside one filter are ORed and separate filters are ANDed, so one filter per token keeps SUP-17 from
        // offering every SUP-x and every x-17. Lower case because the wildcard clause skips the analyser. The key rule
        // has already limited the key to letters, digits, underscores and one hyphen.
        filters.AddRange(KeyTokens(issueKey).Select(t => (Filter)new TextFilter(alias, [t], false)));

        var candidates = new List<IContent>();
        for (var skip = 0; skip < MaxHitsExamined; skip += PageSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await searcher.SearchAsync(
                Constants.Search.IndexAliases.DraftContent,
                query: null,
                filters: filters,
                culture: culture,
                // Members-only pages are candidates too, as in the 17/18 InternalIndex, so a protected duplicate still
                // stops the run. 19.0.0-rc1's draft index returns them either way (ProtectedHolder_IsFound); this keeps
                // that true if the provider starts filtering. The match is still checked by the authoriser.
                accessContext: AccessContext.BypassProtection(),
                skip: skip,
                take: PageSize);

            // Fail closed, before loading anything: a partial candidate list could hide a duplicate.
            if (page.Total > MaxHitsExamined)
            {
                return ([], ActionFailures.Validation($"More than {MaxHitsExamined} nodes look like {issueKey}; use a longer or more specific key."));
            }

            // A hit carries only the content key: one batch load per page (pitfalls P3, P4, P9).
            var keys = page.Documents.Select(d => d.Id).ToArray();
            if (keys.Length > 0)
            {
                candidates.AddRange(_contentService.GetByIds(keys));
            }

            if (page.Total <= skip + PageSize)
            {
                return (candidates, null);
            }
        }

        // Only reached if the index grew past the ceiling while paging.
        return ([], ActionFailures.Validation($"More than {MaxHitsExamined} nodes look like {issueKey}; use a longer or more specific key."));
    }

    // A project key may hold underscores (SUP_X-17). The analyser keeps "sup_x" as one token, so a separate "x"
    // filter would never match; only the part before the first underscore is used, which is a prefix of the indexed
    // token whether or not the analyser splits on the underscore. The exact recheck covers what this loosens.
    // The analyser also drops English stop words, so a project key such as IT, BE or A is never indexed and a filter
    // on it would find nothing; those tokens are left out. A token with an underscore is kept ("it_x" is indexed
    // whole). The issue number is never a stop word, so at least one filter always remains.
    internal static IEnumerable<string> KeyTokens(string issueKey) =>
        TokenSplit().Split(issueKey.ToLowerInvariant())
            .Where(t => t.Length > 0 && (t.Contains('_') || !StopWords.Contains(t)))
            .Select(t => t.Split('_')[0])
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal);

    // Lucene's English stop set (StandardAnalyzer.STOP_WORDS_SET), which Examine's default analyser applies.
    private static readonly FrozenSet<string> StopWords = FrozenSet.Create(
        StringComparer.Ordinal,
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "for", "if", "in", "into", "is", "it", "no", "not",
        "of", "on", "or", "such", "that", "the", "their", "then", "there", "these", "they", "this", "to", "was",
        "will", "with");

    [GeneratedRegex("[^a-z0-9_]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenSplit();
}
#endif
