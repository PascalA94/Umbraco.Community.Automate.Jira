using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Umbraco.Community.Automate.Jira.Configuration;
using Umbraco.Community.Automate.Jira.Webhooks;

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

    // Plan P5: the signature code compares only with FixedTimeEquals. The banned-API analyser stops SequenceEqual
    // at build time; this IL scan also catches string comparisons, which the analyser cannot ban package-wide.
    [Fact]
    public void JiraSignature_ComparesWithFixedTimeEqualsOnly()
    {
        var calls = CallsIn(typeof(JiraSignature)).ToList();

        Assert.Contains(calls, IsFixedTimeEquals);
        Assert.Empty(calls.Where(IsForbiddenComparison).Select(m => $"{m.DeclaringType}.{m.Name}"));
    }

    [Fact]
    public void JiraSignature_DetectorCatchesForbiddenComparisons()
    {
        var calls = CallsIn(typeof(BadSignature)).ToList();

        Assert.Contains(calls, m => m.Name == nameof(Enumerable.SequenceEqual) && m.DeclaringType == typeof(Enumerable));
        Assert.Contains(calls, m => m.Name == nameof(MemoryExtensions.SequenceEqual) && m.DeclaringType == typeof(MemoryExtensions));
        Assert.Contains(calls, m => m.Name == "op_Equality" && m.DeclaringType == typeof(string));
        Assert.Contains(calls, m => m.Name == nameof(string.CompareOrdinal));
        Assert.Equal(4, calls.Count(IsForbiddenComparison));
        Assert.DoesNotContain(calls, IsFixedTimeEquals);
    }

    private static bool IsFixedTimeEquals(MethodBase m) =>
        m.DeclaringType == typeof(CryptographicOperations) && m.Name == nameof(CryptographicOperations.FixedTimeEquals);

    private static bool IsForbiddenComparison(MethodBase m) =>
        (m.DeclaringType == typeof(string) && m.Name is "Equals" or "op_Equality" or "op_Inequality" or "Compare" or "CompareOrdinal")
        || (m.DeclaringType == typeof(Enumerable) && m.Name == nameof(Enumerable.SequenceEqual))
        || (m.DeclaringType == typeof(MemoryExtensions) && m.Name == nameof(MemoryExtensions.SequenceEqual));

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    // Every method body on the type and its compiler-generated nested types (lambdas, local functions).
    private static IEnumerable<MethodBase> CallsIn(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var types = new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        foreach (var t in types)
        {
            var methods = t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all));
            foreach (var method in methods)
            {
                foreach (var called in CallsIn(method))
                {
                    yield return called;
                }
            }
        }
    }

    private static IEnumerable<MethodBase> CallsIn(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var i = 0;
        while (i < il.Length)
        {
            OpCode op;
            if (il[i] == 0xFE)
            {
                op = OpCodesByValue[unchecked((short)(0xFE00 | il[i + 1]))];
                i += 2;
            }
            else
            {
                op = OpCodesByValue[il[i]];
                i += 1;
            }

            switch (op.OperandType)
            {
                case OperandType.InlineMethod:
                    var token = BitConverter.ToInt32(il, i);
                    i += 4;
                    yield return method.Module.ResolveMethod(
                        token,
                        method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                        method.IsGenericMethod ? method.GetGenericArguments() : null)!;
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    i += 8;
                    break;
                case OperandType.InlineSwitch:
                    i += 4 + (4 * BitConverter.ToInt32(il, i));
                    break;
                default:
                    i += 4;
                    break;
            }
        }
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

    // Detector self-test only: every comparison the signature code must never use. Not in the package assembly.
    private static class BadSignature
    {
        // Explicit: with C# 14 first-class spans, a.SequenceEqual(b) on arrays binds to MemoryExtensions instead.
        public static bool Bytes(byte[] a, byte[] b) => Enumerable.SequenceEqual<byte>(a, b);

        public static bool Spans(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

        public static bool Text(string a, string b) => a == b;

        public static int Ordinal(string a, string b) => string.CompareOrdinal(a, b);
    }

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
