#if AUTOMATE_19
using System.Reflection;
using Moq;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Security;
using Umbraco.Automate.Core.Settings;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Search;
using Umbraco.Cms.Core.Search.Querying;
using Umbraco.Cms.Core.Search.Querying.Faceting;
using Umbraco.Cms.Core.Search.Querying.Filtering;
using Umbraco.Cms.Core.Search.Querying.Sorting;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.Automate.Jira.Actions;

namespace Umbraco.Community.Automate.Jira.Tests;

/// <summary>
/// Line 19: input checks, the Umbraco Search call shape, paging, the ceiling and authorisation, with the searcher and
/// the content services stubbed. Matching against a real analysed index is in the integration project.
/// </summary>
public class FindContentByKeyActionSearchTests
{
    private static readonly Guid ArticleKey = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>One search call, as the action made it.</summary>
    private sealed record SearchCall(string IndexAlias, string? Query, IReadOnlyList<Filter> Filters, string? Culture, AccessContext? Access, int Skip, int Take);

    // Hand-written: ISearcher has one method with eleven optional parameters, which makes Moq setups noisy.
    private sealed class RecordingSearcher(Func<SearchCall, SearchResult> respond) : ISearcher
    {
        public List<SearchCall> Calls { get; } = [];

        public Task<SearchResult> SearchAsync(
            string indexAlias,
            string? query = null,
            IEnumerable<Filter>? filters = null,
            IEnumerable<Facet>? facets = null,
            IEnumerable<Sorter>? sorters = null,
            string? culture = null,
            string? segment = null,
            AccessContext? accessContext = null,
            int skip = 0,
            int take = 10,
            int maxSuggestions = 0)
        {
            var call = new SearchCall(indexAlias, query, filters?.ToList() ?? [], culture, accessContext, skip, take);
            Calls.Add(call);
            return Task.FromResult(respond(call));
        }
    }

    private static SearchResult Hits(long total, IEnumerable<Guid> keys) =>
        new(total, keys.Select(k => new Document(k, UmbracoObjectTypes.Document)).ToList(), []);

    private sealed class Rig
    {
        public Rig(
            IReadOnlyList<IContent>? contents = null,
            bool authorised = true,
            ContentVariation propertyVariation = ContentVariation.Nothing,
            Func<SearchCall, SearchResult>? respond = null,
            bool noSearcher = false)
        {
            Contents = contents ?? [Node("Spring", "SUP-17")];
            Searcher = new RecordingSearcher(respond ?? (call => Hits(Contents.Count, call.Skip == 0 ? Contents.Select(c => c.Key) : [])));

            Searchers = new Mock<ISearcherResolver>();
            Searchers.Setup(s => s.GetSearcher(It.IsAny<string>())).Returns((ISearcher?)null);
            if (!noSearcher)
            {
                Searchers.Setup(s => s.GetSearcher(Constants.Search.IndexAliases.DraftContent)).Returns(Searcher);
            }

            Languages = new Mock<ILanguageService>();
            Languages.Setup(l => l.GetAllAsync()).ReturnsAsync(new[]
            {
                Mock.Of<ILanguage>(l => l.IsoCode == "en-GB" && l.IsDefault == false),
                Mock.Of<ILanguage>(l => l.IsoCode == "da-DK" && l.IsDefault == true),
            });

            ContentService = new Mock<IContentService>();
            ContentService.Setup(s => s.GetByIds(It.IsAny<IEnumerable<Guid>>()))
                .Returns<IEnumerable<Guid>>(keys => keys.Select(k => Contents.FirstOrDefault(c => c.Key == k)).OfType<IContent>().ToList());

            var property = Mock.Of<IPropertyType>(p => p.Alias == "jiraKey" && p.Variations == propertyVariation);
            var type = Mock.Of<IContentType>(t => t.Key == ArticleKey && t.Alias == "article" && t.CompositionPropertyTypes == new[] { property });
            ContentTypes = new Mock<IContentTypeService>();
            ContentTypes.Setup(s => s.Get(ArticleKey)).Returns(type);

            Authorizer = new Mock<IAutomationActionAuthorizer>();
            Authorizer.Setup(a => a.AuthorizeContentAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(authorised ? AutomationAuthorizationResult.Success : AutomationAuthorizationResult.Fail("Outside the service account's start node."));
        }

        public IReadOnlyList<IContent> Contents { get; }

        public RecordingSearcher Searcher { get; }

        public Mock<ISearcherResolver> Searchers { get; }

        public Mock<ILanguageService> Languages { get; }

        public Mock<IContentService> ContentService { get; }

        public Mock<IContentTypeService> ContentTypes { get; }

        public Mock<IAutomationActionAuthorizer> Authorizer { get; }

        public async Task<ActionResult> RunAsync(string issueKey = "SUP-17", string? contentTypes = null, string propertyAlias = "jiraKey")
        {
            var action = new FindContentByKeyAction(
                new ActionInfrastructure(Mock.Of<IEditableModelResolver>()),
                Searchers.Object,
                Languages.Object,
                ContentService.Object,
                ContentTypes.Object,
                Authorizer.Object);
            var context = new ActionContext
            {
                AutomationId = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                StepId = Guid.NewGuid(),
                ActionAlias = action.Alias,
                Settings = new FindContentByKeySettings { IssueKey = issueKey, PropertyAlias = propertyAlias, ContentTypes = contentTypes ?? ArticleKey.ToString() },
                Action = action,
                BindingData = new Dictionary<string, object?>(),
            };
            return await action.ExecuteAsync(context, CancellationToken.None);
        }

        public void AssertNoQuery()
        {
            Searchers.Verify(s => s.GetSearcher(It.IsAny<string>()), Times.Never);
            Assert.Empty(Searcher.Calls);
        }
    }

    private static IContent Node(string name, string? key)
    {
        var content = new Mock<IContent>();
        content.SetupGet(c => c.Id).Returns(Random.Shared.Next(2000, int.MaxValue));
        content.SetupGet(c => c.Key).Returns(Guid.NewGuid());
        content.SetupGet(c => c.Name).Returns(name);
        content.SetupGet(c => c.Trashed).Returns(false);
        content.SetupGet(c => c.Published).Returns(false);
        content.SetupGet(c => c.UpdateDate).Returns(new DateTime(2026, 11, 11, 9, 30, 0, DateTimeKind.Utc));
        content.SetupGet(c => c.ContentType).Returns(Mock.Of<ISimpleContentType>(t => t.Key == ArticleKey && t.Alias == "article"));
        content.Setup(c => c.GetValue("jiraKey", null, null, false)).Returns(key);
        return content.Object;
    }

    private static IReadOnlyList<TextFilter> TextFilters(SearchCall call) => call.Filters.OfType<TextFilter>().ToList();

    [Fact]
    public void Attribute_AliasGroupAndPermissions()
    {
        var attribute = typeof(FindContentByKeyAction).GetCustomAttribute<ActionAttribute>()!;

        Assert.Equal("community.jira.findContentByKey", attribute.Alias);
        Assert.Equal("Find content by Jira key", attribute.Name);
        Assert.Equal("Content", attribute.Group);
        Assert.Null(attribute.ConnectionTypeAlias);
        Assert.Contains("content", attribute.RequiredSections!);
        Assert.NotEmpty(attribute.RequiredPermissions!);
        // A site that has not rebuilt its indexes after upgrading finds nothing; the description says so.
        Assert.Contains("rebuild the Umbraco Search indexes", attribute.Description, StringComparison.Ordinal);

        Assert.DoesNotContain(typeof(FindContentByKeyAction).GetInterfaces(), i => i.Name == "ICmsAction");
    }

    [Fact]
    public void Settings_OnlyIssueKeyTakesBindings()
    {
        bool Binds(string name) => typeof(FindContentByKeySettings).GetProperty(name)!.GetCustomAttribute<FieldAttribute>()!.SupportsBindings;

        Assert.True(Binds(nameof(FindContentByKeySettings.IssueKey)));
        Assert.False(Binds(nameof(FindContentByKeySettings.PropertyAlias)));
        Assert.False(Binds(nameof(FindContentByKeySettings.ContentTypes)));
        Assert.Equal("jiraKey", new FindContentByKeySettings().PropertyAlias);
    }

    [Fact]
    public async Task OneMatch_ReturnsTheNode()
    {
        var rig = new Rig();

        var result = await rig.RunAsync(" sup-17 ");

        Assert.Equal(ActionResultStatus.Success, result.Status);
        var output = Assert.IsType<FindContentByKeyOutput>(result.OutputData);
        Assert.Equal(rig.Contents[0].Key, output.ContentKey);
        Assert.Equal("Spring", output.Name);
        Assert.Equal("article", output.ContentTypeAlias);
        Assert.False(output.IsPublished);
        Assert.Equal(rig.Contents[0].UpdateDate, output.UpdateDate);
        // One batch load per page, never a load per hit (pitfalls P3, P4, P9).
        rig.ContentService.Verify(s => s.GetByIds(It.Is<IEnumerable<Guid>>(k => k.SequenceEqual(new[] { rig.Contents[0].Key }))), Times.Once);
        rig.ContentService.Verify(s => s.GetById(It.IsAny<Guid>()), Times.Never);
        rig.ContentService.Verify(s => s.GetById(It.IsAny<int>()), Times.Never);
        rig.Authorizer.Verify(a => a.AuthorizeContentAsync(rig.Contents[0].Key, It.Is<IReadOnlySet<string>>(p => p.Contains(Umbraco.Cms.Core.Actions.ActionBrowse.ActionLetter)), It.IsAny<CancellationToken>()), Times.Once);
    }

    // The draft index, because a page waiting to go live is often unpublished; the default culture, because
    // culture-variant documents have no invariant entry.
    [Fact]
    public async Task Search_UsesDraftIndexAndDefaultCulture()
    {
        var rig = new Rig();

        await rig.RunAsync();

        var call = Assert.Single(rig.Searcher.Calls);
        Assert.Equal("Umb_Content", call.IndexAlias);
        Assert.Null(call.Query);
        Assert.Equal("da-DK", call.Culture);
        // Members-only pages are candidates too, as in the 17/18 InternalIndex: a protected duplicate must still count.
        Assert.True(call.Access?.Bypass, "the search must bypass public access protection");
        Assert.Equal(0, call.Skip);
        Assert.Equal(FindContentByKeyAction.PageSize, call.Take);
    }

    [Fact]
    public async Task Search_Filters_AreTheDesignShape()
    {
        var rig = new Rig();

        await rig.RunAsync("SUP-17");

        var call = Assert.Single(rig.Searcher.Calls);
        var keywords = call.Filters.OfType<KeywordFilter>().ToList();
        Assert.Equal(2, keywords.Count);

        var type = Assert.Single(keywords, k => k.FieldName == Constants.Search.FieldNames.ContentTypeId);
        Assert.False(type.Negate);
        Assert.Equal([ArticleKey.ToString("D")], type.Values);

        var bin = Assert.Single(keywords, k => k.FieldName == Constants.Search.FieldNames.PathIds);
        Assert.True(bin.Negate);
        Assert.Equal([Constants.System.RecycleBinContentKey.ToString("D")], bin.Values);

        var texts = TextFilters(call);
        Assert.Equal(2, texts.Count);
        Assert.All(texts, t => Assert.Equal("jiraKey", t.FieldName));
        Assert.All(texts, t => Assert.False(t.Negate));
        Assert.Equal(["sup", "17"], texts.Select(t => Assert.Single(t.Values)));
        Assert.Equal(4, call.Filters.Count);
    }

    // A project key may hold underscores. The analyser keeps "sup_x" as one token, so a separate "x" filter would
    // never match and the node would be missed; the part before the underscore is a prefix of the indexed token.
    [Fact]
    public async Task Search_UnderscoreKey_FiltersOnThePartBeforeTheUnderscore()
    {
        var rig = new Rig(contents: [Node("A", "SUP_X-17")]);

        var result = await rig.RunAsync("SUP_X-17");

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.Equal(["sup", "17"], TextFilters(Assert.Single(rig.Searcher.Calls)).Select(t => Assert.Single(t.Values)));
    }

    // The analyser drops English stop words, so a project key such as IT is never indexed; filtering on it would find
    // nothing. Only the issue number is filtered on, and the recheck decides.
    [Theory]
    [InlineData("IT-5", new[] { "5" })]
    [InlineData("A-1", new[] { "1" })]
    [InlineData("THE-42", new[] { "42" })]
    [InlineData("IT_X-5", new[] { "it", "5" })]
    [InlineData("ITEM-5", new[] { "item", "5" })]
    public async Task Search_StopWordProjectKey_IsNotAFilter(string key, string[] expected)
    {
        var rig = new Rig(contents: [Node("A", key)]);

        var result = await rig.RunAsync(key);

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.Equal(expected, TextFilters(Assert.Single(rig.Searcher.Calls)).Select(t => Assert.Single(t.Values)));
    }

    // QA-M3-5: index field names are case-sensitive, so the filter uses the property's own alias.
    [Fact]
    public async Task Search_PropertyAlias_UsesTheRealCase()
    {
        var rig = new Rig();

        var result = await rig.RunAsync(propertyAlias: "JIRAKEY");

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.All(TextFilters(Assert.Single(rig.Searcher.Calls)), t => Assert.Equal("jiraKey", t.FieldName));
    }

    [Fact]
    public async Task Search_NoSearcher_ConfigurationError()
    {
        var rig = new Rig(noSearcher: true);

        var result = await rig.RunAsync();

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        Assert.Null(result.OutputData);
        Assert.Empty(rig.Searcher.Calls);
        rig.Authorizer.Verify(x => x.AuthorizeContentAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The index matches by token prefix, so SUP-17 also proposes SUP-170 and "sup-17 old"; only the exact value counts.
    [Fact]
    public async Task Search_OverMatchingCandidates_AreRejectedByRecheck()
    {
        var holder = Node("C", "SUP-17");
        var rig = new Rig(contents: [Node("A", "SUP-170"), Node("B", "sup-17 old"), holder]);

        var result = await rig.RunAsync();

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.Equal(holder.Key, Assert.IsType<FindContentByKeyOutput>(result.OutputData).ContentKey);
    }

    [Fact]
    public async Task Search_Paging_ContinuesUntilTotal()
    {
        var holder = Node("Last page", "SUP-17");
        var rig = new Rig(
            contents: [holder],
            respond: call => Hits(250, call.Skip == 200 ? [holder.Key] : Enumerable.Range(0, 100).Select(_ => Guid.NewGuid())));

        var result = await rig.RunAsync();

        Assert.Equal([0, 100, 200], rig.Searcher.Calls.Select(c => c.Skip));
        Assert.All(rig.Searcher.Calls, c => Assert.Equal(100, c.Take));
        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.Equal(holder.Key, Assert.IsType<FindContentByKeyOutput>(result.OutputData).ContentKey);
    }

    // Fail closed: a partial candidate list could hide a duplicate, so past the ceiling nothing is returned, and the
    // first page's total decides it before any content is loaded.
    [Theory]
    [InlineData(2001)]
    [InlineData(5000)]
    public async Task Search_AboveCeiling_FailsClosed(long total)
    {
        var holder = Node("Holder", "SUP-17");
        var rig = new Rig(
            contents: [holder],
            respond: call => Hits(total, call.Skip == 0 ? [holder.Key, .. Enumerable.Range(0, 99).Select(_ => Guid.NewGuid())] : Enumerable.Range(0, 100).Select(_ => Guid.NewGuid())));

        var result = await rig.RunAsync();

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("More than 2000", result.Exception!.Message, StringComparison.Ordinal);
        Assert.Null(result.OutputData);
        Assert.Single(rig.Searcher.Calls);
        rig.ContentService.Verify(s => s.GetByIds(It.IsAny<IEnumerable<Guid>>()), Times.Never);
        rig.Authorizer.Verify(x => x.AuthorizeContentAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // The ceiling itself is still examined in full: exactly 2000 hits is 20 pages, then the recheck decides.
    [Fact]
    public async Task Search_AtCeiling_ExaminesEveryPage()
    {
        var holder = Node("Last page", "SUP-17");
        var rig = new Rig(
            contents: [holder],
            respond: call => Hits(FindContentByKeyAction.MaxHitsExamined, call.Skip == 1900 ? [holder.Key] : Enumerable.Range(0, 100).Select(_ => Guid.NewGuid())));

        var result = await rig.RunAsync();

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.Equal(20, rig.Searcher.Calls.Count);
        rig.ContentService.Verify(s => s.GetByIds(It.IsAny<IEnumerable<Guid>>()), Times.Exactly(20));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" , ")]
    public async Task NoContentTypes_FailsBeforeQuery(string contentTypes)
    {
        var rig = new Rig();

        var result = await rig.RunAsync(contentTypes: contentTypes);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        rig.AssertNoQuery();
    }

    [Theory]
    [InlineData("22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222")]
    [InlineData("not-a-guid")]
    public async Task OnlyStaleContentTypes_ConfigurationError(string contentTypes)
    {
        var rig = new Rig();

        var result = await rig.RunAsync(contentTypes: contentTypes);

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        rig.AssertNoQuery();
    }

    [Fact]
    public async Task PropertyMissingOnType_ConfigurationError()
    {
        var rig = new Rig();

        var result = await rig.RunAsync(propertyAlias: "otherKey");

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        rig.AssertNoQuery();
    }

    [Fact]
    public async Task VariantProperty_ConfigurationError()
    {
        var rig = new Rig(propertyVariation: ContentVariation.Culture);

        var result = await rig.RunAsync();

        Assert.Equal(StepRunErrorCategory.ConfigurationError, result.ErrorCategory);
        Assert.Contains("invariant", result.Exception!.Message, StringComparison.OrdinalIgnoreCase);
        rig.AssertNoQuery();
    }

    // The key rule keeps hostile characters away from the index; TextFilter with hostile input was never spiked.
    [Theory]
    [InlineData("SUP-17\" OR x:*")]
    [InlineData("*")]
    [InlineData("SUP-17 OR 1")]
    [InlineData("10001")]
    [InlineData("SUP-0")]
    [InlineData("SUP-1?")]
    [InlineData("SUP-1\\")]
    [InlineData("")]
    public async Task IssueKey_Injection_FailsBeforeQuery(string issueKey)
    {
        var rig = new Rig();

        var result = await rig.RunAsync(issueKey);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        rig.AssertNoQuery();
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("1a")]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("a\"")]
    public async Task PropertyAlias_Invalid_FailsBeforeQuery(string alias)
    {
        var rig = new Rig();

        var result = await rig.RunAsync(propertyAlias: alias);

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        rig.AssertNoQuery();
    }

    [Fact]
    public async Task NotAuthorised_ReturnsAuthorizerFailure()
    {
        var rig = new Rig(authorised: false);

        var result = await rig.RunAsync();

        Assert.NotEqual(ActionResultStatus.Success, result.Status);
        Assert.Null(result.OutputData);
        rig.Authorizer.Verify(a => a.AuthorizeContentAsync(rig.Contents[0].Key, It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // Design 6.7 step 4: counted before permissions, so a duplicate the service account cannot see still blocks.
    [Fact]
    public async Task TwoMatches_FailsListingKeys_BeforeAuthorising()
    {
        var a = Node("A", "SUP-17");
        var b = Node("B", "sup-17 ");
        var rig = new Rig(contents: [a, b]);

        var result = await rig.RunAsync();

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains(a.Key.ToString(), result.Exception!.Message);
        Assert.Contains(b.Key.ToString(), result.Exception!.Message);
        Assert.Null(result.OutputData);
        rig.Authorizer.Verify(x => x.AuthorizeContentAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // SEC-M3-6: a non-breaking space stays part of the token, so the exact check must not trim it either.
    [Fact]
    public async Task ValueWithNonBreakingSpace_IsNotAHolder()
    {
        var rig = new Rig(contents: [Node("A", "SUP-17 ")]);

        var result = await rig.RunAsync();

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
    }

    [Fact]
    public async Task ValueWithAsciiSpaces_IsAHolder()
    {
        var rig = new Rig(contents: [Node("A", " SUP-17\t")]);

        var result = await rig.RunAsync();

        Assert.Equal(ActionResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task NoMatch_FailsNotFound()
    {
        var rig = new Rig(contents: [Node("SUP-17", null)]);

        var result = await rig.RunAsync();

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("No ", result.Exception!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MakesNoHttpCalls()
    {
        var parameters = typeof(FindContentByKeyAction).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(parameters, t => t.Namespace?.StartsWith("Umbraco.Community.Automate.Jira.Client", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(parameters, t => t == typeof(IHttpClientFactory) || t == typeof(HttpClient));
    }
}
#endif
