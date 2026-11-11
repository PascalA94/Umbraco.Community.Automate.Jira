using System.Reflection;
using Examine;
using Examine.Search;
using Moq;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Security;
using Umbraco.Automate.Core.Settings;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Community.Automate.Jira.Actions;

namespace Umbraco.Community.Automate.Jira.Tests;

/// <summary>
/// Input checks, call shape and authorisation only, with Examine and the content services mocked. Matching against a
/// real index is in the integration project (FindContentByKeyIntegrationTests).
/// </summary>
public class FindContentByKeyActionTests
{
    private static readonly Guid ArticleKey = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Rig
    {
        public Rig(int totalHits = 1, IReadOnlyList<IContent>? contents = null, bool authorised = true, ContentVariation propertyVariation = ContentVariation.Nothing)
        {
            Contents = contents ?? [Node("Spring", "SUP-17")];

            var results = new Mock<ISearchResults>();
            results.SetupGet(r => r.TotalItemCount).Returns(totalHits);
            var hits = Contents.Select(c => Mock.Of<ISearchResult>(r => r.Id == c.Id.ToString())).ToList();
            results.Setup(r => r.GetEnumerator()).Returns(() => hits.GetEnumerator());

            var operation = new Mock<IBooleanOperation>();
            operation.Setup(o => o.Execute(It.IsAny<QueryOptions?>())).Returns(results.Object);
            Query = new Mock<IQuery>();
            Query.Setup(q => q.NativeQuery(It.IsAny<string>())).Callback<string>(q => NativeQueries.Add(q)).Returns(operation.Object);
            var searcher = new Mock<ISearcher>();
            searcher.Setup(s => s.CreateQuery(It.IsAny<string?>(), It.IsAny<BooleanOperation>())).Returns(Query.Object);
            var index = Mock.Of<IIndex>(i => i.Searcher == searcher.Object);
            Examine = new Mock<IExamineManager>();
            Examine.Setup(m => m.TryGetIndex("InternalIndex", out index)).Returns(true);

            ContentService = new Mock<IContentService>();
            ContentService.Setup(s => s.GetByIds(It.IsAny<IEnumerable<int>>())).Returns(() => Contents);

            var property = Mock.Of<IPropertyType>(p => p.Alias == "jiraKey" && p.Variations == propertyVariation);
            var type = Mock.Of<IContentType>(t => t.Key == ArticleKey && t.Alias == "article" && t.CompositionPropertyTypes == new[] { property });
            ContentTypes = new Mock<IContentTypeService>();
            ContentTypes.Setup(s => s.Get(ArticleKey)).Returns(type);

            Authorizer = new Mock<IAutomationActionAuthorizer>();
            Authorizer.Setup(a => a.AuthorizeContentAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(authorised ? AutomationAuthorizationResult.Success : AutomationAuthorizationResult.Fail("Outside the service account's start node."));
        }

        public IReadOnlyList<IContent> Contents { get; }

        public List<string> NativeQueries { get; } = [];

        public Mock<IQuery> Query { get; }

        public Mock<IExamineManager> Examine { get; }

        public Mock<IContentService> ContentService { get; }

        public Mock<IContentTypeService> ContentTypes { get; }

        public Mock<IAutomationActionAuthorizer> Authorizer { get; }

        public async Task<ActionResult> RunAsync(string issueKey = "SUP-17", string? contentTypes = null, string propertyAlias = "jiraKey")
        {
            var action = new FindContentByKeyAction(
                new ActionInfrastructure(Mock.Of<IEditableModelResolver>()),
                Examine.Object,
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

        public void AssertNoQuery() => Examine.Verify(m => m.TryGetIndex(It.IsAny<string>(), out It.Ref<IIndex>.IsAny), Times.Never);
    }

    private static int _nextId = 2000;

    private static IContent Node(string name, string? key)
    {
        var id = Interlocked.Increment(ref _nextId);
        var content = new Mock<IContent>();
        content.SetupGet(c => c.Id).Returns(id);
        content.SetupGet(c => c.Key).Returns(Guid.NewGuid());
        content.SetupGet(c => c.Name).Returns(name);
        content.SetupGet(c => c.Trashed).Returns(false);
        content.SetupGet(c => c.Published).Returns(false);
        content.SetupGet(c => c.UpdateDate).Returns(new DateTime(2026, 11, 11, 9, 30, 0, DateTimeKind.Utc));
        content.SetupGet(c => c.ContentType).Returns(Mock.Of<ISimpleContentType>(t => t.Key == ArticleKey && t.Alias == "article"));
        content.Setup(c => c.GetValue("jiraKey", null, null, false)).Returns(key);
        return content.Object;
    }

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

        // Design 6.7: it only reads, so it is not ICmsAction (the audit marker for actions that modify content).
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
    }

    [Fact]
    public async Task Query_IsTheDesignShape()
    {
        var rig = new Rig();

        await rig.RunAsync();

        Assert.Equal("+__IndexType:content +jiraKey:\"sup-17\" +(__NodeTypeAlias:article)", Assert.Single(rig.NativeQueries));
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

    // Fail closed: the built-in silently drops stale keys and then searches every type.
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

    // QA-M3-5: Lucene field names are case-sensitive, so the query uses the property's own alias.
    [Fact]
    public async Task PropertyAlias_DifferentCase_QueriesTheRealAlias()
    {
        var rig = new Rig();

        var result = await rig.RunAsync(propertyAlias: "JIRAKEY");

        Assert.Equal(ActionResultStatus.Success, result.Status);
        Assert.Equal("+__IndexType:content +jiraKey:\"sup-17\" +(__NodeTypeAlias:article)", Assert.Single(rig.NativeQueries));
    }

    // QA-M3-7: the hits' IDs are what is loaded, and the Browse permission is what is checked.
    [Fact]
    public async Task LoadsTheHitsAndChecksBrowse()
    {
        var rig = new Rig();
        IEnumerable<int>? loaded = null;
        rig.ContentService.Setup(s => s.GetByIds(It.IsAny<IEnumerable<int>>())).Callback<IEnumerable<int>>(ids => loaded = ids.ToList()).Returns(() => rig.Contents);

        await rig.RunAsync();

        Assert.Equal([rig.Contents[0].Id], loaded);
        rig.Authorizer.Verify(a => a.AuthorizeContentAsync(rig.Contents[0].Key, It.Is<IReadOnlySet<string>>(p => p.Contains(Umbraco.Cms.Core.Actions.ActionBrowse.ActionLetter)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NineCandidates_StillVerified()
    {
        var rig = new Rig(totalHits: 9);

        var result = await rig.RunAsync();

        Assert.Equal(ActionResultStatus.Success, result.Status);
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

    [Theory]
    [InlineData("SUP-17\" OR x:*")]
    [InlineData("*")]
    [InlineData("SUP-17 OR 1")]
    [InlineData("10001")]
    [InlineData("SUP-0")]
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
    public async Task TenCandidates_FailsTooMany()
    {
        var rig = new Rig(totalHits: 10);

        var result = await rig.RunAsync();

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains("too many", result.Exception!.Message, StringComparison.OrdinalIgnoreCase);
        rig.ContentService.Verify(s => s.GetByIds(It.IsAny<IEnumerable<int>>()), Times.Never);
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
        var rig = new Rig(totalHits: 2, contents: [a, b]);

        var result = await rig.RunAsync();

        Assert.Equal(StepRunErrorCategory.Validation, result.ErrorCategory);
        Assert.Contains(a.Key.ToString(), result.Exception!.Message);
        Assert.Contains(b.Key.ToString(), result.Exception!.Message);
        Assert.Null(result.OutputData);
        rig.Authorizer.Verify(x => x.AuthorizeContentAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Pitfalls P3, P4, P9: one index query and one batch load, no tree walk, no per-node loads.
    [Fact]
    public async Task UsesOneQueryAndOneBatchLoad()
    {
        var rig = new Rig(totalHits: 3, contents: [Node("A", "SUP-17"), Node("B", "SUP-170"), Node("C", "SUP-17 old")]);

        await rig.RunAsync();

        rig.Query.Verify(q => q.NativeQuery(It.IsAny<string>()), Times.Once);
        rig.ContentService.Verify(s => s.GetByIds(It.IsAny<IEnumerable<int>>()), Times.Once);
        rig.ContentService.Verify(s => s.GetById(It.IsAny<int>()), Times.Never);
        rig.ContentService.Verify(s => s.GetById(It.IsAny<Guid>()), Times.Never);
        rig.ContentService.Verify(s => s.GetPagedDescendants(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), out It.Ref<long>.IsAny, It.IsAny<Umbraco.Cms.Core.Persistence.Querying.IQuery<IContent>?>(), It.IsAny<Ordering?>()), Times.Never);
    }

    // SEC-M3-6: the index does not split on a non-breaking space, so the exact check must not trim it either, or a
    // value the index never offers as "SUP-17" could count as a holder in one place and not the other.
    [Fact]
    public async Task ValueWithNonBreakingSpace_IsNotAHolder()
    {
        var rig = new Rig(contents: [Node("A", "SUP-17\u00A0")]);

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
        var rig = new Rig(totalHits: 1, contents: [Node("SUP-17", null)]);

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
