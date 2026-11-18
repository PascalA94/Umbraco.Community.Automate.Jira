using NUnit.Framework;

// No parallelism setting existed (NUnit's default is none). Stated explicitly because HangWatchdogAttribute ends the whole
// test host when one test hangs, which is only intentional while a single test runs at a time.
[assembly: Parallelizable(ParallelScope.None)]

// Deliberately outside any namespace, so NUnit runs it once for the whole assembly.
// Umbraco's own GlobalSetupTeardown is a namespace-less SetUpFixture too, but NUnit only applies a SetUpFixture to
// tests in its own assembly. Without this, Tests:Database is never read and every fixture fails with
// "Unsupported test database provider" (the spike Task 2 step 3a UNKNOWN).
[SetUpFixture]
public sealed class JiraIntegrationGlobalSetUp : GlobalSetupTeardown
{
#if AUTOMATE_19
    // Umbraco Search's Lucene directory (umbraco/Data/TEMP/ExamineIndexes under the project folder, the harness's
    // content root) survives between runs, while each run starts a fresh database with new keys. A second run then
    // got stale documents back from Umb_Content. Wiping it before any host starts makes every run index from empty.
    [OneTimeSetUp]
    public void DeleteStaleSearchIndexes()
    {
        var folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (folder is not null && folder.GetFiles("*.IntegrationTests.csproj").Length == 0)
        {
            folder = folder.Parent;
        }

        if (folder is null)
        {
            throw new InvalidOperationException("The integration test project folder was not found above " + TestContext.CurrentContext.TestDirectory);
        }

        var indexes = Path.Combine(folder.FullName, "umbraco", "Data", "TEMP", "ExamineIndexes");
        if (Directory.Exists(indexes))
        {
            Directory.Delete(indexes, recursive: true);
        }
    }
#endif
}
