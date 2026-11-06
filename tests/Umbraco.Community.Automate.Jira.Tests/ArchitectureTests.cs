using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Umbraco.Community.Automate.Jira.Configuration;

namespace Umbraco.Community.Automate.Jira.Tests;

/// <summary>
/// Pitfalls P2 and S1 as tripwires over the package assembly only. The demo site has an intentional static
/// (M0 review F15), so it is never scanned here.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Package = typeof(JiraComposer).Assembly;

    [Fact]
    public void NoMutableStaticFields()
    {
        Assert.Empty(FindMutableStatics(SafeTypes(Package)));
    }

    [Fact]
    public void NoMutableStaticFields_DetectorCatchesOffenders()
    {
        var offenders = FindMutableStatics([typeof(Offenders), typeof(Allowed)]);

        Assert.Contains(offenders, o => o.Contains(nameof(Offenders.Cache)));
        Assert.Contains(offenders, o => o.Contains(nameof(Offenders.Counter)));
        Assert.Contains(offenders, o => o.Contains(nameof(Offenders.ReadonlyButMutable)));
        Assert.DoesNotContain(offenders, o => o.Contains(nameof(Allowed)));
    }

    [Fact]
    public void NoControllers()
    {
        var controllers = SafeTypes(Package).Where(t => DerivesFrom(t, "Microsoft.AspNetCore.Mvc.ControllerBase")).Select(t => t.FullName);

        Assert.Empty(controllers);
    }

    [Fact]
    public void NoControllers_DetectorFollowsTheBaseChain()
    {
        Assert.True(DerivesFrom(typeof(Child), typeof(Base).FullName!));
        Assert.False(DerivesFrom(typeof(Allowed), typeof(Base).FullName!));
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static bool DerivesFrom(Type type, string fullName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName == fullName)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> FindMutableStatics(IEnumerable<Type> types)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var found = new List<string>();
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(flags))
            {
                if (field.IsLiteral || IsCompilerGenerated(field) || IsCompilerGenerated(type))
                {
                    continue;
                }

                if (field.IsInitOnly && IsImmutable(field.FieldType))
                {
                    continue;
                }

                found.Add($"{type.FullName}.{field.Name} ({field.FieldType.Name})");
            }
        }

        return found;
    }

    // The [GeneratedRegex] source generator emits static caches; they are compiler output, not our state.
    private static bool IsCompilerGenerated(MemberInfo member)
    {
        for (var current = member; current is not null; current = current.DeclaringType)
        {
            if (current.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                || current.IsDefined(typeof(System.CodeDom.Compiler.GeneratedCodeAttribute), inherit: false))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsImmutable(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(Guid)
        || type == typeof(TimeSpan)
        || type == typeof(DateTimeOffset)
        || type == typeof(Uri)
        || type == typeof(Regex)
        || type == typeof(System.Text.Json.JsonSerializerOptions)
        || type.Namespace is "System.Collections.Frozen" or "System.Collections.Immutable"
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Buffers.SearchValues<>));

    private static class Offenders
    {
        public static readonly Dictionary<string, string> ReadonlyButMutable = [];
        public static Dictionary<string, string> Cache = [];
        public static int Counter = 1;
    }

    private static class Allowed
    {
        public const string Name = "constant";
        public static readonly string Text = "immutable";
    }

    private class Base;

    private sealed class Child : Base;
}
