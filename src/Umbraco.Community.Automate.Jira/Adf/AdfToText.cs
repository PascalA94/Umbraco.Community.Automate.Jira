using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>
/// ADF to plain text, for outputs such as a webhook's comment body (design 7.5): blocks and list items on their own
/// lines, marks dropped, links as their text, mentions as their display text.
/// </summary>
internal static class AdfToText
{
    private static readonly FrozenSet<string> BlockTypes = new[]
    {
        "paragraph", "heading", "bulletList", "orderedList", "listItem", "codeBlock", "blockquote", "rule", "panel",
        "table", "tableRow", "tableHeader", "tableCell", "expand", "nestedExpand", "taskList", "taskItem",
        "decisionList", "decisionItem", "mediaSingle", "mediaGroup", "blockCard", "embedCard", "layoutSection", "layoutColumn",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Flattens <paramref name="adf"/>. Null gives an empty string, and a plain JSON string is returned as it is,
    /// because Jira can send a comment body either way. Uses an explicit stack, so no input depth can exhaust the call stack.
    /// </summary>
    public static string Flatten(JsonNode? adf)
    {
        if (adf is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            return value.GetValue<string>();
        }

        if (adf is not JsonObject root)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var stack = new Stack<JsonObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var type = Read(node["type"]);

            if (type is not null && BlockTypes.Contains(type) && builder.Length > 0 && builder[^1] != '\n')
            {
                builder.Append('\n');
            }

            switch (type)
            {
                case "text":
                    builder.Append(Read(node["text"]));
                    continue;
                case "hardBreak":
                    builder.Append('\n');
                    continue;
                case "mention" or "emoji" or "status" or "placeholder":
                    builder.Append(Read(node["attrs"]?["text"]) ?? Read(node["attrs"]?["shortName"]));
                    continue;
                case "inlineCard":
                    builder.Append(Read(node["attrs"]?["url"]));
                    continue;
            }

            if (node["content"] is JsonArray children)
            {
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    if (children[i] is JsonObject child)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        return builder.ToString().Trim('\n');
    }

    private static string? Read(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
