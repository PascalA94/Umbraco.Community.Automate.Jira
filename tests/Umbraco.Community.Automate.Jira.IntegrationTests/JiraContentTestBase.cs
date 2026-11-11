using Examine;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Infrastructure.Examine.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Integration.Umbraco.Examine.Lucene.UmbracoExamine;

namespace Umbraco.Community.Automate.Jira.IntegrationTests;

/// <summary>
/// Real InternalIndex and IContentService (Umbraco's integration harness, SQLite), with helpers that save content
/// and wait until the index has caught up.
/// </summary>
public abstract class JiraContentTestBase : ExamineBaseTest
{
    // The harness registers no site indexes by default; add Umbraco's real ones so the action finds "InternalIndex"
    // through IExamineManager exactly as on a site.
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.AddExamineIndexes();
    }

    protected IContentService ContentService => GetRequiredService<IContentService>();

    protected IContentTypeService ContentTypeService => GetRequiredService<IContentTypeService>();

    // The Lucene index outlives each test's fresh database and node IDs restart, so documents from earlier tests
    // stay in the index (clearing it races the queued indexing). Each test gets its own type alias, so the
    // action's type clause never sees them; a reused ID is simply re-indexed with the new node.
    protected async Task<IContentType> CreateArticleTypeAsync(string? alias = null, string propertyAlias = "jiraKey", ContentVariation propertyVariation = ContentVariation.Nothing)
    {
        alias ??= "article" + Guid.NewGuid().ToString("N")[..8];
        var textstring = await GetRequiredService<IDataTypeService>().GetAsync(Constants.DataTypes.Guids.TextstringGuid);
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), -1)
        {
            Alias = alias,
            Name = alias,
            Variations = propertyVariation == ContentVariation.Nothing ? ContentVariation.Nothing : ContentVariation.Culture,
            AllowedAsRoot = true,
        };
        type.AddPropertyType(new PropertyType(GetRequiredService<IShortStringHelper>(), textstring!, propertyAlias)
        {
            Name = propertyAlias,
            Variations = propertyVariation,
        });
        var result = await ContentTypeService.CreateAsync(type, Constants.Security.SuperUserKey);
        if (!result.Success)
        {
            throw new InvalidOperationException("Content type not created: " + result.Result);
        }

        return type;
    }

    protected static Content NewContent(IContentType type, string name, string? key, string propertyAlias = "jiraKey")
    {
        var content = new Content(name, -1, type);
        if (key is not null)
        {
            content.SetValue(propertyAlias, key);
        }

        return content;
    }

    protected async Task<IContent> SaveAsync(IContent content)
    {
        ContentService.Save(content);
        await IndexAsync(content);
        return content;
    }

    // A site indexes from cache-refresher notifications on a background queue, which the harness does not run.
    // The documents come from the same IContentValueSetBuilder the site's InternalIndex handler uses, so field
    // names and analysis are the real ones.
    protected async Task IndexAsync(IContent content)
    {
        ExamineManager.TryGetIndex(Constants.UmbracoIndexes.InternalIndexName, out var index);
        var valueSets = GetRequiredService<IContentValueSetBuilder>().GetValueSets(ContentService.GetById(content.Key)!).ToList();
        await ExecuteAndWaitForIndexing(() => index!.IndexItems(valueSets), Constants.UmbracoIndexes.InternalIndexName);

        // The completion event can fire before the searcher reopens, so wait until this node's current version (its
        // path changes when it is trashed) is visible to queries.
        var saved = ContentService.GetById(content.Key)!;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var hit = index!.Searcher.CreateQuery().Id(saved.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Execute().FirstOrDefault();
            // __Key as well as __Path: the index outlives test runs, so an older run's document can share this ID.
            if (hit is not null
                && hit.Values.TryGetValue("__Key", out var key) && Guid.TryParse(key, out var indexed) && indexed == saved.Key
                && hit.Values.TryGetValue("__Path", out var path) && path == saved.Path)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Node {saved.Id} did not become searchable in the InternalIndex.");
    }
}
