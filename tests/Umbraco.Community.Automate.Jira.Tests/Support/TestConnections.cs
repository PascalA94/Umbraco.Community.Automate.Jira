using System.Reflection;
using Moq;
using Umbraco.Automate.Core.Connections;

namespace Umbraco.Community.Automate.Jira.Tests.Support;

/// <summary>
/// Builds the <see cref="ConfiguredConnection"/> Automate hands an action. Its constructor is internal, so this uses
/// it through reflection, which keeps the tests on the real type instead of a look-alike.
/// </summary>
internal static class TestConnections
{
    public static ConfiguredConnection Jira(object settings, Guid? id = null)
    {
        var connection = new Umbraco.Automate.Core.Connections.Connection
        {
            Id = id ?? Guid.NewGuid(),
            Alias = "jira",
            Name = "Jira",
            Type = "community.jira.cloud",
        };

        return (ConfiguredConnection)Activator.CreateInstance(
            typeof(ConfiguredConnection),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: [connection, Mock.Of<IConnectionType>(), settings],
            culture: null)!;
    }
}
