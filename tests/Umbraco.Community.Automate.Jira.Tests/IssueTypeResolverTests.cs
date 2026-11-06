using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Automate.Core.Actions;
using Umbraco.Community.Automate.Jira.Client;
using Umbraco.Community.Automate.Jira.Connection;
using Umbraco.Community.Automate.Jira.Tests.Support;

namespace Umbraco.Community.Automate.Jira.Tests;

public class IssueTypeResolverTests
{
    private const string TypesJson = """{"issueTypes":[{"id":"10001","name":"Bug"},{"id":"10002","name":"Task"}],"maxResults":50,"total":2}""";

    private sealed class Rig
    {
        public Rig()
        {
            Acme = TestTargets.Acme();
            Fake = new FakeJiraHandler();
            Provider = TestHost.Compose(primary: Fake);
        }

        public AcmeCredential Acme { get; }

        public FakeJiraHandler Fake { get; }

        public IServiceProvider Provider { get; }

        // A fresh scope each time: the cache must outlive one scope.
        public IIssueTypeResolver Resolver => Provider.CreateScope().ServiceProvider.GetRequiredService<IIssueTypeResolver>();

        public JiraTarget Target(Guid connectionId)
        {
            Assert.True(JiraTarget.TryCreate(Acme.Settings("6f9619ff-8b86-d011-b42d-00c04fc964ff"), Acme.Store, connectionId, out var target, out var error), error);
            return target!;
        }
    }

    [Theory]
    [InlineData("Task", "10002")]
    [InlineData("task", "10002")]
    [InlineData("  BUG ", "10001")]
    public async Task Name_ResolvesCaseInsensitive(string name, string id)
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, TypesJson);

        Assert.Equal(id, await rig.Resolver.ResolveAsync(rig.Target(Guid.NewGuid()), "SBX", name, CancellationToken.None));
        Assert.EndsWith("/rest/api/3/issue/createmeta/SBX/issuetypes", rig.Fake.Requests.Single().Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("maxResults=200", rig.Fake.Requests.Single().Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValuesShape_AlsoRead()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, """{"values":[{"id":"7","name":"Story"}]}""");

        Assert.Equal("7", await rig.Resolver.ResolveAsync(rig.Target(Guid.NewGuid()), "SBX", "Story", CancellationToken.None));
    }

    [Fact]
    public async Task UnknownIssueType_ListsValidTypes()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, TypesJson);

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Resolver.ResolveAsync(rig.Target(Guid.NewGuid()), "SBX", "Story", CancellationToken.None));

        Assert.Equal(StepRunErrorCategory.Validation, ex.Category);
        Assert.Contains("Bug, Task", ex.Message);
        Assert.Contains("SBX", ex.Message);
    }

    [Fact]
    public async Task IssueTypeCache_HitsOnce()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, TypesJson).EnqueueJson(HttpStatusCode.OK, TypesJson);
        var connection = Guid.NewGuid();

        await rig.Resolver.ResolveAsync(rig.Target(connection), "SBX", "Bug", CancellationToken.None);
        await rig.Resolver.ResolveAsync(rig.Target(connection), "sbx", "Task", CancellationToken.None);
        Assert.Single(rig.Fake.Requests);

        await rig.Resolver.ResolveAsync(rig.Target(Guid.NewGuid()), "SBX", "Bug", CancellationToken.None);
        Assert.Equal(2, rig.Fake.Requests.Count);
    }

    [Fact]
    public async Task NumericIssueType_NoRequest()
    {
        var rig = new Rig();

        Assert.Equal("10004", await rig.Resolver.ResolveAsync(rig.Target(Guid.NewGuid()), "SBX", " 10004 ", CancellationToken.None));
        Assert.Empty(rig.Fake.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankIssueType_ValidationNoRequest(string name)
    {
        var rig = new Rig();

        var ex = await Assert.ThrowsAsync<JiraRequestException>(() => rig.Resolver.ResolveAsync(rig.Target(Guid.NewGuid()), "SBX", name, CancellationToken.None));

        Assert.Equal(StepRunErrorCategory.Validation, ex.Category);
        Assert.Empty(rig.Fake.Requests);
    }

    [Fact]
    public async Task FailedLookup_NotCached()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.ServiceUnavailable).EnqueueJson(HttpStatusCode.OK, TypesJson);
        var connection = Guid.NewGuid();

        await Assert.ThrowsAsync<JiraRequestException>(() => rig.Resolver.ResolveAsync(rig.Target(connection), "SBX", "Bug", CancellationToken.None));
        Assert.Equal("10001", await rig.Resolver.ResolveAsync(rig.Target(connection), "SBX", "Bug", CancellationToken.None));
    }

    [Fact]
    public async Task ProjectKey_IsEscapedInPath()
    {
        var rig = new Rig();
        rig.Fake.EnqueueJson(HttpStatusCode.OK, TypesJson);

        await rig.Resolver.ResolveAsync(rig.Target(Guid.NewGuid()), "10010", "Bug", CancellationToken.None);

        Assert.EndsWith("/issue/createmeta/10010/issuetypes", rig.Fake.Requests.Single().Uri.AbsolutePath, StringComparison.Ordinal);
    }
}
