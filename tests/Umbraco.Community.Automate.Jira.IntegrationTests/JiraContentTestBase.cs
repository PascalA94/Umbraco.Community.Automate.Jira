using System.Diagnostics;
using Examine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
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
// 120 s against a normal suite of about 26 s: long enough for a slow runner, short enough to fail before the job timeout.
[HangWatchdog(120)]
public abstract class JiraContentTestBase : ExamineBaseTest
{
    // The harness registers no site indexes by default; add Umbraco's real ones so the action finds "InternalIndex"
    // through IExamineManager exactly as on a site.
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.AddExamineIndexes();
    }

    /// <summary>
    /// Holds each test until Umbraco's unattended package migrations, including Automate's, have finished.
    /// </summary>
    /// <remarks>
    /// Why: the harness calls <c>_host.Start()</c> and returns while UnattendedUpgradeBackgroundService is still running
    /// the package migration plans (Automate's AddAutomateSectionToAdminGroup) on a thread-pool thread. On a fresh database
    /// the first test races those writes for the SQLite write lock on the shared in-memory database, and both sides sit in
    /// Umbraco's transient-retry loop. The hang probe's stacks showed exactly that: the migration thread in
    /// RetryPolicy.ExecuteAction under AddAutomateSectionToAdminGroup, the test thread in SqliteDistributedLock.ObtainWriteLock.
    /// Condition: the service's own <see cref="BackgroundService.ExecuteTask"/>, which completes after the migrations, the
    /// runtime-level change to Run and component initialisation. The service is internal, so it is found by type name.
    /// Not usable instead: Automate's AutomateReadinessSignal (this harness does not compose Automate, so it is not
    /// registered) and IContentRoutingReadiness (never marked ready here, because the harness mocks IRuntime).
    /// Runs after UmbracoIntegrationTest's own [SetUp] (base-class SetUps run first), which starts the host.
    /// </remarks>
    [SetUp]
    public async Task WaitForUnattendedMigrations()
    {
        var runtimeState = GetRequiredService<IRuntimeState>();
        var service = Services.GetServices<IHostedService>().OfType<BackgroundService>()
            .FirstOrDefault(s => s.GetType().Name == "UnattendedUpgradeBackgroundService");
        // Assumption pinned for both legs (floor 17.x, latest 18.x; read in Umbraco 17.8.0; on 18.3.x only the type's presence was confirmed): ExecuteAsync
        // returns only after the package migrations, the runtime-level change and component initialisation, so its
        // completion means the migrations are done. Revisit if a major version moves that work off ExecuteAsync.
        // Throws instead of logging when the service is missing: the static Serilog logger is silent in fixtures that
        // do not set Logger.Serilog, so a warning would let the race come back unnoticed.
        if (service?.ExecuteTask is not { } running)
        {
            throw new InvalidOperationException("UnattendedUpgradeBackgroundService (or its ExecuteTask) was not found in the test host, so the wait for package migrations cannot work. Umbraco probably renamed it; update JiraContentTestBase.WaitForUnattendedMigrations.");
        }

        var wasFinished = running.IsCompleted;
        var timer = Stopwatch.StartNew();
        try
        {
            await running.WaitAsync(StartupTimeout);
        }
        catch (TimeoutException) when (!running.IsCompleted)
        {
            throw new TimeoutException($"Unattended migrations did not finish within {StartupTimeout.TotalSeconds:F0} s of host start (runtime level {runtimeState.Level}).");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unattended migrations failed (runtime level {runtimeState.Level}): {ex.Message}", ex);
        }

        if (runtimeState.Level != RuntimeLevel.Run)
        {
            throw new InvalidOperationException($"Umbraco did not reach the Run level (level {runtimeState.Level}): {runtimeState.BootFailedException}");
        }

        // wasFinished=false means the test body would have started while the migrations were still running.
        Serilog.Log.ForContext<JiraContentTestBase>().Information("[jira-test] {Test}: startup finished after {Elapsed} ms (finished at test start: {WasFinished}); test body starts now", TestContext.CurrentContext.Test.Name, timer.ElapsedMilliseconds, wasFinished);
    }

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

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
