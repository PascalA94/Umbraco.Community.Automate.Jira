using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Umbraco.Community.Automate.Jira.Adf;

namespace Umbraco.Community.Automate.Jira.Actions;

/// <summary>
/// The <c>AdditionalFieldsJson</c> rules (design 6.2, F-04, plan review P6). The setting has no bindings, and is
/// still treated as hostile: only listed keys, no duplicates at any depth, nothing that a typed field sets, and
/// every ADF document inside goes through <see cref="AdfSanitiser"/>.
/// </summary>
internal static partial class AdditionalFields
{
    // Set only by their own typed settings, so they get their own message.
    private static readonly FrozenSet<string> Protected =
        new[] { "project", "issuetype", "summary", "description", "reporter", "security", "parent" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Allowed =
        new[] { "components", "fixVersions", "versions", "duedate", "environment", "labels" }.ToFrozenSet(StringComparer.Ordinal);

    private const int MaxKeyInMessage = 64;

    /// <summary>
    /// Validates <paramref name="additionalFieldsJson"/> and merges it into <paramref name="fields"/>. On any error,
    /// returns false with a message and leaves <paramref name="fields"/> unchanged. Keys are compared ordinally and
    /// case-sensitively after JSON unescaping, so case variants and escaped spellings cannot pass as another key.
    /// </summary>
    public static bool TryMerge(JsonObject fields, string? additionalFieldsJson, ICollection<string> warnings, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(additionalFieldsJson))
        {
            return true;
        }

        // The same input cap as the Markdown and ADF JSON formats (F-11), checked before parsing.
        if (System.Text.Encoding.UTF8.GetByteCount(additionalFieldsJson) > AdfLimits.MaxInputBytes)
        {
            error = $"AdditionalFieldsJson is larger than {AdfLimits.MaxInputBytes / 1024} KB.";
            return false;
        }

        var options = new JsonDocumentOptions { MaxDepth = AdfLimits.MaxJsonDepth };
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(additionalFieldsJson, options);
        }
        catch (JsonException)
        {
            // The exception text quotes the input, so it is not passed on.
            error = $"AdditionalFieldsJson must be a valid JSON object nested at most {AdfLimits.MaxJsonDepth} levels deep.";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "AdditionalFieldsJson must be a JSON object.";
                return false;
            }

            if (JsonGuards.FindDuplicateKey(document.RootElement) is { } duplicate)
            {
                error = $"duplicate key {ForMessage(duplicate)}";
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var key = property.Name;
                if (Protected.Contains(key))
                {
                    error = $"{key} is set by its own field";
                    return false;
                }

                if (!Allowed.Contains(key) && !CustomField().IsMatch(key))
                {
                    error = $"{ForMessage(key)} is not an allowed field. Allowed: customfield_<number>, components, fixVersions, versions, duedate, environment, labels.";
                    return false;
                }

                if (fields.ContainsKey(key))
                {
                    error = $"set {key} in one place";
                    return false;
                }
            }

            // Validated as a whole first, so a failure never leaves a half-merged object.
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = JsonNode.Parse(property.Value.GetRawText(), documentOptions: options);
                SanitiseDocuments(value, warnings);
                fields[property.Name] = value;
            }
        }

        return true;
    }

    // Every value shaped like an ADF document, at any depth, is sanitised in place; its inside is not walked again.
    private static void SanitiseDocuments(JsonNode? root, ICollection<string> warnings)
    {
        var stack = new Stack<JsonNode?>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case JsonObject obj when obj["type"] is JsonValue type && type.GetValueKind() == JsonValueKind.String && type.GetValue<string>() == "doc":
                    AdfSanitiser.Sanitise(obj, warnings);
                    break;
                case JsonObject obj:
                    foreach (var (_, child) in obj)
                    {
                        stack.Push(child);
                    }

                    break;
                case JsonArray array:
                    foreach (var child in array)
                    {
                        stack.Push(child);
                    }

                    break;
            }
        }
    }

    // Keys come from the step's own settings, but are still cut and stripped of control characters for the run log.
    private static string ForMessage(string key)
    {
        var printable = new string(key.Where(c => !char.IsControl(c)).ToArray());
        return printable.Length > MaxKeyInMessage ? printable[..MaxKeyInMessage] : printable;
    }

    [GeneratedRegex(@"^customfield_[0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex CustomField();
}
