using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Umbraco.Community.Automate.Jira.Tests.Support;

/// <summary>
/// Validates ADF against Atlassian's published JSON schema (<c>Fixtures/adf-schema.json</c>, draft-04). A small
/// validator for exactly the keywords that schema uses, so the tests need no schema library: <c>$ref</c>,
/// <c>type</c>, <c>enum</c>, <c>properties</c>, <c>additionalProperties</c>, <c>required</c>, <c>items</c>,
/// <c>minItems</c>, <c>maxItems</c>, <c>minLength</c>, <c>minimum</c>, <c>maximum</c>, <c>pattern</c>,
/// <c>anyOf</c> and <c>allOf</c>. An unknown keyword fails loudly rather than being ignored.
/// </summary>
internal static class AdfSchemaAssert
{
    private static readonly HashSet<string> Known =
    [
        "$ref", "$schema", "type", "enum", "properties", "additionalProperties", "required", "items", "minItems",
        "maxItems", "minLength", "minimum", "maximum", "pattern", "anyOf", "allOf", "description", "definitions",
    ];

    private static readonly Lazy<JsonObject> Schema = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "adf-schema.json")))!.AsObject());

    public static void Valid(JsonNode? doc)
    {
        Assert.NotNull(doc);
        var errors = new List<string>();

        // Validate the serialised form, which is what Jira receives (nodes built in code hold CLR numbers).
        Check(JsonNode.Parse(doc!.ToJsonString()), Schema.Value, "$", errors);
        Assert.True(errors.Count == 0, "ADF does not match the schema:\n" + string.Join("\n", errors.Take(10)) + "\n" + doc!.ToJsonString());
    }

    public static bool IsValid(JsonNode? doc)
    {
        var errors = new List<string>();
        Check(doc is null ? null : JsonNode.Parse(doc.ToJsonString()), Schema.Value, "$", errors);
        return errors.Count == 0;
    }

    private static void Check(JsonNode? value, JsonObject schema, string path, List<string> errors)
    {
        foreach (var keyword in schema.Select(p => p.Key))
        {
            if (!Known.Contains(keyword))
            {
                throw new InvalidOperationException($"Schema keyword '{keyword}' is not supported by AdfSchemaAssert.");
            }
        }

        if (schema["$ref"] is JsonValue reference)
        {
            var name = reference.GetValue<string>().Replace("#/definitions/", string.Empty, StringComparison.Ordinal);
            Check(value, Schema.Value["definitions"]![name]!.AsObject(), path, errors);
            return;
        }

        if (schema["allOf"] is JsonArray allOf)
        {
            foreach (var part in allOf)
            {
                Check(value, part!.AsObject(), path, errors);
            }
        }

        if (schema["anyOf"] is JsonArray anyOf)
        {
            var matched = anyOf.Any(part =>
            {
                var local = new List<string>();
                Check(value, part!.AsObject(), path, local);
                return local.Count == 0;
            });
            if (!matched)
            {
                errors.Add($"{path}: matches none of anyOf ({Describe(value)})");
            }
        }

        if (schema["type"] is { } type && !TypeMatches(value, type))
        {
            errors.Add($"{path}: expected {type.ToJsonString()}, got {Describe(value)}");
            return;
        }

        if (schema["enum"] is JsonArray options && !options.Any(o => JsonNode.DeepEquals(o, value)))
        {
            errors.Add($"{path}: {value?.ToJsonString()} not in {options.ToJsonString()}");
        }

        if (value is JsonObject obj)
        {
            var properties = schema["properties"] as JsonObject;
            foreach (var required in (schema["required"] as JsonArray ?? []).Select(r => r!.GetValue<string>()))
            {
                if (!obj.ContainsKey(required))
                {
                    errors.Add($"{path}: missing required '{required}'");
                }
            }

            foreach (var (key, child) in obj)
            {
                if (properties?[key] is JsonObject propertySchema)
                {
                    Check(child, propertySchema, $"{path}.{key}", errors);
                }
                else if (schema["additionalProperties"] is JsonValue additional && additional.GetValueKind() == JsonValueKind.False)
                {
                    errors.Add($"{path}: property '{key}' is not allowed");
                }
            }
        }

        if (value is JsonArray array)
        {
            if (schema["minItems"] is JsonValue min && array.Count < min.GetValue<int>())
            {
                errors.Add($"{path}: fewer than {min} items");
            }

            if (schema["maxItems"] is JsonValue max && array.Count > max.GetValue<int>())
            {
                errors.Add($"{path}: more than {max} items");
            }

            if (schema["items"] is JsonObject items)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    Check(array[i], items, $"{path}[{i}]", errors);
                }
            }
        }

        if (value is JsonValue scalar)
        {
            if (scalar.GetValueKind() == JsonValueKind.String)
            {
                var text = scalar.GetValue<string>();
                if (schema["minLength"] is JsonValue minLength && text.Length < minLength.GetValue<int>())
                {
                    errors.Add($"{path}: shorter than {minLength}");
                }

                if (schema["pattern"] is JsonValue pattern && !Regex.IsMatch(text, pattern.GetValue<string>()))
                {
                    errors.Add($"{path}: does not match {pattern}");
                }
            }

            if (scalar.GetValueKind() == JsonValueKind.Number)
            {
                var number = scalar.GetValue<double>();
                if (schema["minimum"] is JsonValue minimum && number < minimum.GetValue<double>())
                {
                    errors.Add($"{path}: below {minimum}");
                }

                if (schema["maximum"] is JsonValue maximum && number > maximum.GetValue<double>())
                {
                    errors.Add($"{path}: above {maximum}");
                }
            }
        }
    }

    private static bool TypeMatches(JsonNode? value, JsonNode type) =>
        type is JsonArray many ? many.Any(t => TypeMatches(value, t!)) : type.GetValue<string>() switch
        {
            "object" => value is JsonObject,
            "array" => value is JsonArray,
            "string" => value is JsonValue v && v.GetValueKind() == JsonValueKind.String,
            "number" => value is JsonValue v && v.GetValueKind() == JsonValueKind.Number,
            "integer" => value is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.GetValue<double>() % 1 == 0,
            "boolean" => value is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
            "null" => value is null,
            var other => throw new InvalidOperationException($"Schema type '{other}' is not supported."),
        };

    private static string Describe(JsonNode? value)
    {
        var json = value?.ToJsonString() ?? "null";
        return json.Length > 120 ? json[..120] + "…" : json;
    }
}
