using Examine;
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
/// Plan review P2 and Review Focus F-01: every name-match negative runs against the real InternalIndex and
/// IContentService, so an action that queried the node-name field instead of the property would fail here.
/// </summary>
/// <remarks>
/// One Umbraco host for the fixture: a new host per test reopens the same Lucene directory and its searcher
/// answered stale (passing alone, failing in sequence). Each test makes its own content type, so tests never see
/// each other's nodes through the action's type clause.
/// </remarks>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerFixture)]
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

    private async Task<ActionResult> FindAsync(string issueKey, IContentType? type = null)
    {
        var authorizer = new Mock<IAutomationActionAuthorizer>();
        authorizer.Setup(a => a.AuthorizeContentAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid key, IReadOnlySet<string> _, CancellationToken _) =>
                _denied.Contains(key) ? AutomationAuthorizationResult.Fail("Outside the start node.") : AutomationAuthorizationResult.Success);

        var action = new FindContentByKeyAction(
            new ActionInfrastructure(Mock.Of<IEditableModelResolver>()),
            GetRequiredService<IExamineManager>(),
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
