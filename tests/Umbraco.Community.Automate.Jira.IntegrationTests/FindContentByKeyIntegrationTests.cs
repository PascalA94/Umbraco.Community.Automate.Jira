#if AUTOMATE_19
using Umbraco.Cms.Core.Extensions;
using Umbraco.Cms.Core.Search;
using Umbraco.Cms.Core.Search.Querying.Filtering;
using Umbraco.Cms.Core.Services;
#else
using Examine;
#endif
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Umbraco.Automate.Core.Actions;
using Umbraco.Automate.Core.Security;
using Umbraco.Automate.Core.Settings;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Community.Automate.Jira.Actions;

namespace Umbraco.Community.Automate.Jira.IntegrationTests;

/// <summary>
/// Plan review P2 and Review Focus F-01: every name-match negative runs against the real index (InternalIndex on 17
/// and 18, Umbraco Search's Umb_Content on 19) and IContentService, so an action that queried the node-name field
/// instead of the property would fail here.
/// </summary>
/// <remarks>
/// One Umbraco host for the fixture: a new host per test reopens the same Lucene directory and its searcher
/// answered stale (passing alone, failing in sequence). Each test makes its own content type, so tests never see
/// each other's nodes through the action's type clause.
/// </remarks>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerFixture, Logger = UmbracoTestOptions.Logger.Serilog)]
public sealed class FindContentByKeyIntegrationTests : JiraContentTestBase
{
    private readonly HashSet<Guid> _denied = [];

    private IContentType _article = null!;

    [SetUp]
    public async Task CreateType()
    {
        _denied.Clear();
        _article = await CreateArticleTypeAsync();
    }

#if AUTOMATE_19
    // Text search in Umb_Content with the action's own filter shape, in the default culture as the action does.
    private async Task<long> SearchTotalAsync(params string[] tokens)
    {
        var searcher = GetRequiredService<ISearcherResolver>().GetSearcher(Umbraco.Cms.Core.Constants.Search.IndexAliases.DraftContent)!;
        var culture = (await GetRequiredService<ILanguageService>().GetAllAsync()).First(l => l.IsDefault).IsoCode;
        var filters = new List<Filter>
        {
            new KeywordFilter(Umbraco.Cms.Core.Constants.Search.FieldNames.ContentTypeId, [_article.Key.AsKeyword()], false),
            new KeywordFilter(Umbraco.Cms.Core.Constants.Search.FieldNames.PathIds, [Umbraco.Cms.Core.Constants.System.RecycleBinContentKey.AsKeyword()], true),
        };
        filters.AddRange(tokens.Select(t => (Filter)new TextFilter("jiraKey", [t], false)));
        var result = await searcher.SearchAsync(Umbraco.Cms.Core.Constants.Search.IndexAliases.DraftContent, filters: filters, culture: culture, take: 100);
        return result.Total;
    }

    // Design 6.7's [LOW] on 19: a stock Textstring is indexed in the draft index under its alias, so the action's
    // filter shape proposes a saved (unpublished) node.
    [Test]
    public async Task DraftIndex_ProposesTextstringCandidate()
    {
        await SaveAsync(NewContent(_article, "Spring campaign", "SUP-17"));

        Assert.That(await SearchTotalAsync("sup", "17"), Is.EqualTo(1));
    }

    // Review focus 2: the index matches by token prefix, so SUP-17 also proposes SUP-170 and "SUP-17 old". Only the
    // exact recheck and the duplicate rule decide.
    [Test]
    public async Task PrefixKey_OverMatches_ButOnlyTheExactNodeIsReturned()
    {
        var exact = await SaveAsync(NewContent(_article, "Exact", "SUP-17"));
        await SaveAsync(NewContent(_article, "Longer", "SUP-170"));
        await SaveAsync(NewContent(_article, "Suffix", "SUP-17 old"));

        Assert.That(await SearchTotalAsync("sup", "17"), Is.EqualTo(3), "the index should over-match; if not, this test no longer covers the recheck");
        Assert.That(FoundKey(await FindAsync("SUP-17")), Is.EqualTo(exact.Key));
    }

    // Project keys may hold underscores. The analyser keeps "sup_x" as one token, so the action filters on the part
    // before the underscore ("sup", a prefix of it); a filter on "x" alone would never match.
    [Test]
    public async Task UnderscoreKey_IsFound()
    {
        var holder = await SaveAsync(NewContent(_article, "Underscore", "SUP_X-17"));
        await SaveAsync(NewContent(_article, "Other project", "SUP_Y-17"));

        Assert.That(await SearchTotalAsync("x"), Is.EqualTo(0), "the analyser split on the underscore; KeyTokens' premise changed, recheck the ruling");
        Assert.That(FoundKey(await FindAsync("SUP_X-17")), Is.EqualTo(holder.Key));
    }
    // Security review F1: Umbraco Search leaves protected (members-only) content out unless the search bypasses
    // protection. A protected holder must still be found, and a protected duplicate must still stop the run, as on
    // 17 and 18 where the InternalIndex holds every node.
    private async Task<IContent> ProtectAsync(IContent node)
    {
        var login = await SaveAsync(NewContent(_article, "Login", null));
        var noAccess = await SaveAsync(NewContent(_article, "No access", null));
        var entry = new PublicAccessEntry(node, login, noAccess, [new PublicAccessRule { RuleType = Umbraco.Cms.Core.Constants.Conventions.PublicAccess.MemberRoleRuleType, RuleValue = "Members" }]);
        GetRequiredService<IPublicAccessService>().Save(entry);
        // Re-save so the index picks up the protection.
        return await SaveAsync(ContentService.GetById(node.Key)!);
    }

    [Test]
    public async Task ProtectedHolder_IsFound()
    {
        var holder = await ProtectAsync(await SaveAsync(NewContent(_article, "Members only", "SUP-17")));

        Assert.That(GetRequiredService<IPublicAccessService>().IsProtected(holder).Success, Is.True, "the setup did not protect the node");
        Assert.That(FoundKey(await FindAsync("SUP-17")), Is.EqualTo(holder.Key));
    }

    [Test]
    public async Task ProtectedDuplicate_StillFails()
    {
        var hidden = await ProtectAsync(await SaveAsync(NewContent(_article, "Members only", "SUP-17")));
        var open = await SaveAsync(NewContent(_article, "Open", "SUP-17"));

        var result = await FindAsync("SUP-17");

        Assert.That(result.ErrorCategory, Is.EqualTo(StepRunErrorCategory.Validation));
        Assert.That(result.Exception!.Message, Does.Contain(hidden.Key.ToString()).And.Contain(open.Key.ToString()));
        Assert.That(result.OutputData, Is.Null);
    }

    // The analyser drops English stop words, so a project key such as IT, BE, NO or A is never indexed as a token.
    // A filter on it would match nothing and the holder would be reported missing.
    [TestCase("IT-5")]
    [TestCase("BE-12")]
    [TestCase("A-1")]
    public async Task StopWordProjectKey_IsFound(string key)
    {
        var holder = await SaveAsync(NewContent(_article, "Stop word " + key, key));

        Assert.That(FoundKey(await FindAsync(key)), Is.EqualTo(holder.Key));
    }
#else
    // Design 6.7's [LOW], settled first (plan Task 17a step 1): a Textstring property is indexed under its alias in
    // the InternalIndex, drafts included, so the action's query shape finds a saved (unpublished) node.
    [Test]
    public async Task InternalIndex_IndexesTextstringUnderAlias()
    {
        await SaveAsync(NewContent(_article, "Spring campaign", "SUP-17"));

        Assert.That(ExamineManager.TryGetIndex(Umbraco.Cms.Core.Constants.UmbracoIndexes.InternalIndexName, out var index), Is.True);
        var results = index!.Searcher.CreateQuery().NativeQuery($"+__IndexType:content +jiraKey:\"sup-17\" +(__NodeTypeAlias:{_article.Alias})").Execute();

        Assert.That(results.TotalItemCount, Is.EqualTo(1));
    }
#endif

    private async Task<ActionResult> FindAsync(string issueKey, IContentType? type = null)
    {
        var authorizer = new Mock<IAutomationActionAuthorizer>();
        authorizer.Setup(a => a.AuthorizeContentAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid key, IReadOnlySet<string> _, CancellationToken _) =>
                _denied.Contains(key) ? AutomationAuthorizationResult.Fail("Outside the start node.") : AutomationAuthorizationResult.Success);

        var action = new FindContentByKeyAction(
            new ActionInfrastructure(Mock.Of<IEditableModelResolver>()),
#if AUTOMATE_19
            GetRequiredService<ISearcherResolver>(),
            GetRequiredService<ILanguageService>(),
#else
            GetRequiredService<IExamineManager>(),
#endif
            ContentService,
            ContentTypeService,
            authorizer.Object);
        var context = new ActionContext
        {
            AutomationId = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            StepId = Guid.NewGuid(),
            ActionAlias = action.Alias,
            Settings = new FindContentByKeySettings { IssueKey = issueKey, ContentTypes = (type ?? _article).Key.ToString() },
            Action = action,
            BindingData = new Dictionary<string, object?>(),
        };
        return await action.ExecuteAsync(context, CancellationToken.None);
    }

    private static Guid FoundKey(ActionResult result)
    {
        Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Success), result.Exception?.Message);
        return ((FindContentByKeyOutput)result.OutputData!).ContentKey;
    }

    private static void AssertNotFound(ActionResult result)
    {
        Assert.That(result.Status, Is.Not.EqualTo(ActionResultStatus.Success));
        Assert.That(result.ErrorCategory, Is.EqualTo(StepRunErrorCategory.Validation));
        Assert.That(result.Exception!.Message, Does.StartWith("No "));
        Assert.That(result.OutputData, Is.Null);
    }

    [Test]
    public async Task OneMatch_ReturnsContentKey()
    {
        var page = await SaveAsync(NewContent(_article, "Spring campaign", "SUP-17"));

        Assert.That(FoundKey(await FindAsync(" sup-17 ")), Is.EqualTo(page.Key));
    }

    [Test]
    public async Task NodeNamedLikeKey_EmptyProperty_NotFound()
    {
        await SaveAsync(NewContent(_article, "SUP-17", null));

        AssertNotFound(await FindAsync("SUP-17"));
    }

    [Test]
    public async Task NodeNamedLikeKey_OtherNodeHoldsKey_ReturnsTheHolder()
    {
        await SaveAsync(NewContent(_article, "SUP-17", null));
        var holder = await SaveAsync(NewContent(_article, "Other", "SUP-17"));

        Assert.That(FoundKey(await FindAsync("SUP-17")), Is.EqualTo(holder.Key));
    }

    [Test]
    public async Task PartialValues_NotMatched()
    {
        foreach (var value in new[] { "SUP-170", "SUP-17 old", "XSUP-17", "SUP-17-1" })
        {
            await SaveAsync(NewContent(_article, "Node " + value, value));
        }

        AssertNotFound(await FindAsync("SUP-17"));
    }

    [Test]
    public async Task LuceneSpecialValues_NotMatched()
    {
        foreach (var value in new[] { "SUP-17*", "SUP?17", "\"SUP-17\"", "SUP-17~", "SUP\\-17" })
        {
            await SaveAsync(NewContent(_article, "Node " + value, value));
        }

        AssertNotFound(await FindAsync("SUP-17"));

        var exact = await SaveAsync(NewContent(_article, "Exact", "SUP-17"));
        Assert.That(FoundKey(await FindAsync("SUP-17")), Is.EqualTo(exact.Key));
    }

    [Test]
    public async Task TrashedNode_Ignored()
    {
        var trashed = await SaveAsync(NewContent(_article, "Old page", "SUP-17"));
        ContentService.MoveToRecycleBin(trashed);
        await IndexAsync(trashed);

        AssertNotFound(await FindAsync("SUP-17"));

        var live = await SaveAsync(NewContent(_article, "New page", "SUP-17"));
        Assert.That(FoundKey(await FindAsync("SUP-17")), Is.EqualTo(live.Key));
    }

    [Test]
    public async Task TwoMatches_FailsListingKeys_NoContentKey()
    {
        var a = await SaveAsync(NewContent(_article, "A", "SUP-17"));
        var b = await SaveAsync(NewContent(_article, "B", "sup-17"));

        var result = await FindAsync("SUP-17");

        Assert.That(result.ErrorCategory, Is.EqualTo(StepRunErrorCategory.Validation));
        Assert.That(result.Exception!.Message, Does.Contain(a.Key.ToString()).And.Contain(b.Key.ToString()));
        Assert.That(result.OutputData, Is.Null);
    }

    [Test]
    public async Task TwoMatches_OneOutsideStartNode_StillFails()
    {
        var visible = await SaveAsync(NewContent(_article, "Visible", "SUP-17"));
        var hidden = await SaveAsync(NewContent(_article, "Hidden", "SUP-17"));
        _denied.Add(hidden.Key);

        var result = await FindAsync("SUP-17");

        Assert.That(result.ErrorCategory, Is.EqualTo(StepRunErrorCategory.Validation));
        Assert.That(result.Exception!.Message, Does.Contain(visible.Key.ToString()).And.Contain(hidden.Key.ToString()));
        Assert.That(result.OutputData, Is.Null);
    }

    [Test]
    public async Task OutsideStartNode_ReturnsAuthorizerFailure()
    {
        var page = await SaveAsync(NewContent(_article, "Hidden", "SUP-17"));
        _denied.Add(page.Key);

        var result = await FindAsync("SUP-17");

        Assert.That(result.Status, Is.Not.EqualTo(ActionResultStatus.Success));
        Assert.That(result.OutputData, Is.Null);
    }

    [Test]
    public async Task VariantProperty_ConfigurationError()
    {
        var variant = await CreateArticleTypeAsync(propertyVariation: ContentVariation.Culture);

        var result = await FindAsync("SUP-17", variant);

        Assert.That(result.ErrorCategory, Is.EqualTo(StepRunErrorCategory.ConfigurationError));
    }
}
