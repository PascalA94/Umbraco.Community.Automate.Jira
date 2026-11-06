using System.Text.Json;

namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>Checks on JSON from outside that <c>System.Text.Json</c> does not make for us.</summary>
internal static class JsonGuards
{
    /// <summary>
    /// The first property name repeated within one object, at any depth, or null. <c>JsonNode</c> only notices a
    /// duplicate lazily, by throwing an <see cref="ArgumentException"/> that quotes the key, so callers check this
    /// first on a <see cref="JsonDocument"/>. Names are already unescaped. An explicit stack, never recursion.
    /// </summary>
    public static string? FindDuplicateKey(JsonElement root)
    {
        var stack = new Stack<JsonElement>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var element = stack.Pop();
            if (element.ValueKind == JsonValueKind.Object)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!seen.Add(property.Name))
                    {
                        return property.Name;
                    }

                    stack.Push(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    stack.Push(item);
                }
            }
        }

        return null;
    }
}
