using System.Text.Json.Nodes;

namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>
/// Plain text to ADF: blank lines start a paragraph, single newlines become hard breaks, and nothing else is
/// interpreted, so it is safe for any bound value (design 7.5).
/// </summary>
internal sealed class PlainTextAdfConverter : IAdfConverter
{
    public AdfResult Convert(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new AdfResult(null, []);
        }

        var lines = input.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var content = new JsonArray();
        JsonArray? inline = null;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                inline = null;
                continue;
            }

            if (inline is null)
            {
                inline = [];
                content.Add(new JsonObject { ["type"] = "paragraph", ["content"] = inline });
            }
            else
            {
                inline.Add(new JsonObject { ["type"] = "hardBreak" });
            }

            inline.Add(new JsonObject { ["type"] = "text", ["text"] = line });
        }

        var doc = new JsonObject { ["type"] = "doc", ["version"] = 1, ["content"] = content };
        var warnings = new List<string>();
        AdfSanitiser.Sanitise(doc, warnings);
        return new AdfResult(doc, warnings);
    }
}
