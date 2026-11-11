using NUnit.Framework;

// Deliberately outside any namespace, so NUnit runs it once for the whole assembly.
// Umbraco's own GlobalSetupTeardown is a namespace-less SetUpFixture too, but NUnit only applies a SetUpFixture to
// tests in its own assembly. Without this, Tests:Database is never read and every fixture fails with
// "Unsupported test database provider" (the spike Task 2 step 3a UNKNOWN).
[SetUpFixture]
public sealed class JiraIntegrationGlobalSetUp : GlobalSetupTeardown
{
}
