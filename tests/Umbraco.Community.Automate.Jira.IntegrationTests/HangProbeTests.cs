using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Tests.Common.Testing;

namespace Umbraco.Community.Automate.Jira.IntegrationTests;

/// <summary>
/// Proves the hang watchdog fires on the symptom seen in CI: a content save blocked by a write transaction that
/// another connection holds open on the shared-cache test database (SQLite error 6, retried by Umbraco's RetryPolicy).
/// The lock is taken inside the test body, so it is always after JiraContentTestBase has waited for the unattended
/// package migrations to finish; the blocked statement is therefore the save's write lock, not a migration.
/// </summary>
/// <remarks>
/// [Explicit] because it is meant to fail. Run it with
/// <c>dotnet test tests/Umbraco.Community.Automate.Jira.IntegrationTests --filter "Name~Save_BlockedByLockOnAnotherConnection"</c>
/// and expect a timeout failure plus a stacks-*.txt file under hang-diagnostics.
/// </remarks>
[TestFixture]
[Explicit("Deliberately hangs to prove the watchdog and timeout; expected to fail.")]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerFixture, Logger = UmbracoTestOptions.Logger.Serilog)]
public sealed class HangProbeTests : JiraContentTestBase
{
    [Test]
    public async Task Save_BlockedByLockOnAnotherConnection()
    {
        var type = await CreateArticleTypeAsync();
        var connectionString = GetRequiredService<IOptionsMonitor<ConnectionStrings>>().CurrentValue.ConnectionString!;

        using var holder = new SqliteConnection(connectionString);
        holder.Open();
        using var command = holder.CreateCommand();
        command.CommandText = "BEGIN IMMEDIATE";
        command.ExecuteNonQuery();

        // The open write transaction owns the shared-cache database, so the save's first insert gets error 6 and is retried.
        await SaveAsync(NewContent(type, "Blocked", "SUP-1"));
    }
}
