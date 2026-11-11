using System.Text.Json;
using Umbraco.Automate.Core.Automations;
using Umbraco.Automate.Core.Automations.Transfer;
using Umbraco.Automate.Core.Workspaces;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Umbraco.Community.Automate.Jira.TestSite.Seeds;

/// <summary>Demo only, never packed: seeds the use case 4 recipe (design 6.8) and the content type it needs.</summary>
public sealed class SeedComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, UseCase4Seeder>();
}

/// <summary>
/// Creates the <c>article</c> document type (invariant Textstring <c>jiraKey</c>) and imports
/// <c>usecase4.automation.json</c> as an unpublished draft, which Automate never runs (F-01, council condition 6).
/// </summary>
/// <remarks>
/// The import needs a workspace whose service account may publish only a "Jira pages" subtree (design 6.8). That
/// is a deliberate manual step, so this seeder never creates a workspace or a service account: it imports only
/// once a workspace with the alias <c>jira-pages</c> exists, and logs what to do otherwise.
/// </remarks>
public sealed class UseCase4Seeder(
    IContentTypeService contentTypes,
    IDataTypeService dataTypes,
    IShortStringHelper shortStrings,
    IAutomationService automations,
    IWorkspaceService workspaces,
    IWebHostEnvironment environment,
    ILogger<UseCase4Seeder> logger) : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    /// <summary>The <c>article</c> type's key; the seed's Find steps name it.</summary>
    public static readonly Guid ArticleTypeKey = Guid.Parse("6a1e0c2b-3d4f-4a5b-8c6d-7e8f9a0b1c2d");

    private const string WorkspaceAlias = "jira-pages";
    private const string AutomationAlias = "jiraUseCase4";

    /// <inheritdoc />
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        // SEC-M3-7: demo seeding only ever runs on a developer's machine.
        if (!environment.IsDevelopment())
        {
            return;
        }

        await EnsureArticleTypeAsync();

        if (await automations.GetAutomationByAliasAsync(AutomationAlias, cancellationToken) is not null)
        {
            return;
        }

        var path = Path.Combine(environment.ContentRootPath, "Seeds", "usecase4.automation.json");
        using var seed = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(path, cancellationToken));

        // The seed must stay disabled (UseCase4SeedTests pins it); refuse rather than import a live recipe.
        if (!seed.RootElement.TryGetProperty("enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False)
        {
            logger.LogError("Use case 4 seed not imported: its enabled flag is not false.");
            return;
        }

        if (await workspaces.GetWorkspaceByAliasAsync(WorkspaceAlias, cancellationToken) is not { } workspace)
        {
            logger.LogInformation(
                "Use case 4 seed not imported yet: create a workspace with the alias {Alias} whose service account can publish only the Jira pages subtree, then restart.",
                WorkspaceAlias);
            return;
        }

        try
        {
            // The export writes enums as names (errorBehavior "Terminate"), so read them the same way.
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
            var model = seed.RootElement.Deserialize<AutomationExportModel>(options)!;

            // Imported, never published: an unpublished automation does not run.
            await automations.ImportAutomationAsync(model, workspace.Id, null, Constants.Security.SuperUserKey, cancellationToken);
            logger.LogInformation("Use case 4 seed imported as an unpublished draft into {Alias}.", WorkspaceAlias);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            // A demo seed must never stop the site starting.
            logger.LogError(ex, "Use case 4 seed not imported.");
        }
    }

    private async Task EnsureArticleTypeAsync()
    {
        if (contentTypes.Get(ArticleTypeKey) is not null)
        {
            return;
        }

        var textstring = await dataTypes.GetAsync(Constants.DataTypes.Guids.TextstringGuid);
        var article = new ContentType(shortStrings, -1)
        {
            Key = ArticleTypeKey,
            Alias = "article",
            Name = "Article",
            AllowedAsRoot = true,
            Variations = ContentVariation.Nothing,
        };
        article.AddPropertyType(new PropertyType(shortStrings, textstring!, "jiraKey")
        {
            Name = "Jira key",
            Description = "The Jira issue key this page belongs to, for example SBX-17. Must be unique.",
            Variations = ContentVariation.Nothing,
        });
        await contentTypes.CreateAsync(article, Constants.Security.SuperUserKey);
    }
}
