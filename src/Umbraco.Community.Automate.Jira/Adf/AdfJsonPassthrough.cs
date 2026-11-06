using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>
/// ADF JSON supplied by the user: checked for the <c>{ "type": "doc", "version": 1, "content": [...] }</c> shape and
/// then put through the same <see cref="AdfSanitiser"/> pass as the other formats (design 7.5).
/// </summary>
internal sealed class AdfJsonPassthrough : IAdfConverter
{
    public AdfResult Convert(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new AdfResult(null, []);
        }

        if (Encoding.UTF8.GetByteCount(input) > AdfLimits.MaxInputBytes)
        {
            throw new AdfInputTooLargeException($"The ADF JSON is larger than {AdfLimits.MaxInputBytes / 1024} KB.");
        }

        JsonNode? root;
        try
        {
            var options = new JsonDocumentOptions { MaxDepth = AdfLimits.MaxJsonDepth };
            using (var document = JsonDocument.Parse(input, options))
            {
                // Checked first: JsonNode would throw later, quoting the key (M1 security review M1).
                if (JsonGuards.FindDuplicateKey(document.RootElement) is not null)
                {
                    throw new AdfFormatException("The ADF JSON repeats a property name inside one object.");
                }
            }

            root = JsonNode.Parse(input, documentOptions: options);
        }
        catch (JsonException)
        {
            // JsonException text quotes the input around the error position, so it is never passed on.
            throw new AdfFormatException($"The ADF JSON could not be read. It must be valid JSON nested at most {AdfLimits.MaxJsonDepth} levels deep.");
        }

        if (root is not JsonObject doc
            || doc["type"] is not JsonValue type || type.GetValueKind() != JsonValueKind.String || type.GetValue<string>() != "doc"
            || doc["version"] is not JsonValue version || version.GetValueKind() != JsonValueKind.Number || !version.TryGetValue<int>(out var v) || v != 1
            || doc["content"] is not JsonArray)
        {
            throw new AdfFormatException("The ADF JSON must be an object with \"type\": \"doc\", \"version\": 1 and a \"content\" array.");
        }

        var warnings = new List<string>();
        AdfSanitiser.Sanitise(doc, warnings);
        return new AdfResult(doc, warnings);
    }
}

/// <summary>ADF JSON input that is not JSON, or not an ADF document. Mapped to a <c>Validation</c> failure; never echoes the input.</summary>
public sealed class AdfFormatException(string message) : Exception(message);
