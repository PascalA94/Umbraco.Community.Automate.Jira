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
}
