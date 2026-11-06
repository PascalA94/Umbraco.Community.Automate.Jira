using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>
/// The single safety pass every ADF value goes through before it reaches Jira (design 7.5, F-08). It is an
/// allowlist: the document is rebuilt from scratch, keeping only the nodes, marks and attributes listed below,
/// and anything else is unwrapped to its text with a warning. Never a failure.
/// </summary>
/// <remarks>
/// The rebuild also keeps the output inside Atlassian's ADF schema (for example, a heading inside a list item
/// becomes a paragraph, and an empty list is dropped), because Jira rejects a document the schema rejects.
/// Warnings name a node or mark type only, never the content or a link address, since they go to the run log.
/// </remarks>
internal static partial class AdfSanitiser
{
    private static readonly FrozenSet<string> BlockTypes =
        new[] { "paragraph", "heading", "bulletList", "orderedList", "listItem", "codeBlock", "blockquote", "rule" }.ToFrozenSet(StringComparer.Ordinal);

    // Inline node types from the ADF schema. Found at block level, they are gathered into a paragraph.
    private static readonly FrozenSet<string> InlineTypes =
        new[] { "text", "hardBreak", "mention", "emoji", "inlineCard", "date", "status", "placeholder", "inlineExtension", "mediaInline" }.ToFrozenSet(StringComparer.Ordinal);

    private enum Context
    {
        Doc,

        // Blockquotes and list items take the same blocks: paragraphs, lists and code blocks.
        Nested,
    }

    /// <summary>Rebuilds <paramref name="doc"/> in place and adds one line per kind of change to <paramref name="warnings"/>.</summary>
    public static void Sanitise(JsonObject doc, ICollection<string> warnings)
    {
        var walker = new Walker();
        var content = walker.Blocks(Children(doc), Context.Doc, depth: 1);

        doc.Clear();
        doc["type"] = "doc";
        doc["version"] = 1;
        doc["content"] = new JsonArray([.. content]);

        foreach (var warning in walker.Warnings)
        {
            warnings.Add(warning);
        }
    }

    /// <summary>
    /// The link address to keep, or null. Trimmed, free of control characters, and an absolute URI with scheme
    /// http, https or mailto. A prefix check is not enough: <c>Uri</c> decides what the scheme really is.
    /// </summary>
    internal static string? SafeHref(JsonNode? href)
    {
        if (href is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }

        var trimmed = value.GetValue<string>().Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsControl) || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme switch
        {
            "http" or "https" when uri.Host.Length > 0 => trimmed,
            "mailto" => trimmed,
            _ => null,
        };
    }

    private static IEnumerable<JsonNode?> Children(JsonNode node) => node["content"] as JsonArray ?? [];

    private static string TypeOf(JsonObject node) =>
        node["type"] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : string.Empty;

    // Type names are echoed into warnings, so anything unusual is reported generically.
    private static string SafeName(string type) => SafeTypeName().IsMatch(type) ? type : "unknown";

    private static double? ReadNumber(JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var i))
        {
            return i;
        }

        if (value.TryGetValue<long>(out var l))
        {
            return l;
        }

        return value.TryGetValue<double>(out var d) ? d : value.TryGetValue<decimal>(out var m) ? (double)m : null;
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]{0,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTypeName();

    [GeneratedRegex(@"^[A-Za-z0-9+#.-]{1,32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodeLanguage();

    /// <summary>One pass over one document. Holds the text budget and the warnings, so it is never shared.</summary>
    private sealed class Walker
    {
        private readonly List<string> _warnings = [];
        private int _budget = AdfLimits.MaxTextLength - AdfLimits.TruncatedSuffix.Length;

        public IReadOnlyList<string> Warnings => _warnings;

        // Set once the text cap is reached: everything after the cut is left out.
        private bool Stopped { get; set; }

        public List<JsonObject> Blocks(IEnumerable<JsonNode?> items, Context context, int depth)
        {
            var output = new List<JsonObject>();
            JsonArray? loose = null;
            foreach (var item in items)
            {
                if (Stopped)
                {
                    break;
                }

                if (item is not JsonObject node)
                {
                    Warn("Removed content that is not an ADF node.");
                    continue;
                }

                var type = TypeOf(node);
                if (InlineTypes.Contains(type))
                {
                    // Inline content at block level is gathered into one paragraph.
                    if (loose is null)
                    {
                        loose = [];
                        output.Add(new JsonObject { ["type"] = "paragraph", ["content"] = loose });
                    }

                    foreach (var inline in Inline([node], depth))
                    {
                        loose.Add(inline);
                    }

                    continue;
                }

                loose = null;
                output.AddRange(Block(node, type, context, depth));
            }

            // A gathering paragraph whose content was all removed is noise.
            output.RemoveAll(b => b["content"] is JsonArray { Count: 0 });
            return output;
        }

        private IEnumerable<JsonObject> Block(JsonObject node, string type, Context context, int depth)
        {
            if (depth > AdfLimits.MaxNesting)
            {
                Warn($"Flattened content nested more than {AdfLimits.MaxNesting} levels deep.");
                return TextParagraph(NodeText(node));
            }

            switch (type)
            {
                case "paragraph":
                    WarnMarks(node);
                    return [WithContent(new JsonObject { ["type"] = "paragraph" }, Inline(Children(node), depth))];

                case "heading":
                {
                    WarnMarks(node);
                    var level = ReadNumber(node["attrs"]?["level"]);
                    var valid = level is >= 1 and <= 6 && level % 1 == 0;
                    if (!valid)
                    {
                        Warn("Turned a heading with an invalid level into a paragraph.");
                    }

                    // The schema allows headings only at the top level.
                    return valid && context == Context.Doc
                        ? [WithContent(new JsonObject { ["type"] = "heading", ["attrs"] = new JsonObject { ["level"] = (int)level!.Value } }, Inline(Children(node), depth))]
                        : [WithContent(new JsonObject { ["type"] = "paragraph" }, Inline(Children(node), depth))];
                }

                case "bulletList":
                case "orderedList":
                {
                    var listItems = ListItems(Children(node), depth + 1);
                    if (listItems.Count == 0)
                    {
                        return [];
                    }

                    var list = new JsonObject { ["type"] = type };
                    if (type == "orderedList" && ReadNumber(node["attrs"]?["order"]) is { } order && order >= 0 && order % 1 == 0 && order <= int.MaxValue)
                    {
                        list["attrs"] = new JsonObject { ["order"] = (int)order };
                    }

                    list["content"] = new JsonArray([.. listItems]);
                    return [list];
                }

                case "listItem":
                    // A list item outside a list: keep what it holds.
                    return Blocks(Children(node), context, depth);

                case "codeBlock":
                {
                    var code = new JsonObject { ["type"] = "codeBlock" };
                    if (ReadString(node["attrs"]?["language"]) is { } language && CodeLanguage().IsMatch(language))
                    {
                        code["attrs"] = new JsonObject { ["language"] = language };
                    }

                    var text = Take(PlainText(node));
                    if (!string.IsNullOrEmpty(text))
                    {
                        code["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
                    }

                    return [code];
                }

                case "blockquote":
                {
                    if (context != Context.Doc)
                    {
                        // The schema does not allow a quote inside a quote or a list item.
                        return Blocks(Children(node), context, depth + 1);
                    }

                    var inner = Blocks(Children(node), Context.Nested, depth + 1);
                    return inner.Count == 0 ? [] : [new JsonObject { ["type"] = "blockquote", ["content"] = new JsonArray([.. inner]) }];
                }

                case "rule":
                    return context == Context.Doc ? [new JsonObject { ["type"] = "rule" }] : [];

                default:
                    Warn($"Unwrapped unsupported content '{SafeName(type)}'.");
                    return node["content"] is JsonArray
                        ? Blocks(Children(node), context, depth + 1)
                        : TextParagraph(LeafText(node));
            }
        }

        private List<JsonObject> ListItems(IEnumerable<JsonNode?> items, int depth)
        {
            var output = new List<JsonObject>();
            foreach (var item in items)
            {
                if (Stopped)
                {
                    break;
                }

                if (item is not JsonObject node)
                {
                    continue;
                }

                // Anything that is not a list item is wrapped in one, so the list stays valid.
                var blocks = TypeOf(node) == "listItem"
                    ? Blocks(Children(node), Context.Nested, depth + 1)
                    : Blocks([node], Context.Nested, depth + 1);
                if (blocks.Count > 0)
                {
                    output.Add(new JsonObject { ["type"] = "listItem", ["content"] = new JsonArray([.. blocks]) });
                }
            }

            return output;
        }

        private List<JsonObject> Inline(IEnumerable<JsonNode?> items, int depth)
        {
            var output = new List<JsonObject>();
            foreach (var item in items)
            {
                if (Stopped)
                {
                    break;
                }

                if (item is not JsonObject node)
                {
                    continue;
                }

                var type = TypeOf(node);
                switch (type)
                {
                    case "text":
                    {
                        var raw = ReadString(node["text"]);
                        if (string.IsNullOrEmpty(raw))
                        {
                            continue;
                        }

                        var marks = Marks(node["marks"]);
                        var text = Take(raw)!;
                        var textNode = new JsonObject { ["type"] = "text", ["text"] = text };
                        if (marks is not null)
                        {
                            textNode["marks"] = marks;
                        }

                        output.Add(textNode);
                        break;
                    }

                    case "hardBreak":
                        output.Add(new JsonObject { ["type"] = "hardBreak" });
                        break;

                    default:
                    {
                        // Block nodes inside a paragraph are a shape error, not unsafe content, so no warning.
                        if (!BlockTypes.Contains(type))
                        {
                            Warn($"Unwrapped unsupported content '{SafeName(type)}'.");
                        }

                        var text = Take(NodeText(node));
                        if (!string.IsNullOrEmpty(text))
                        {
                            output.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                        }

                        break;
                    }
                }
            }

            return output;
        }

        private JsonArray? Marks(JsonNode? marks)
        {
            if (marks is not JsonArray array)
            {
                return null;
            }

            var kept = new List<JsonObject>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array)
            {
                if (item is not JsonObject mark)
                {
                    continue;
                }

                var type = TypeOf(mark);
                switch (type)
                {
                    case "strong" or "em" or "strike" or "code":
                        if (seen.Add(type))
                        {
                            kept.Add(new JsonObject { ["type"] = type });
                        }

                        break;

                    case "link":
                        if (SafeHref(mark["attrs"]?["href"]) is { } href)
                        {
                            if (seen.Add(type))
                            {
                                kept.Add(new JsonObject { ["type"] = "link", ["attrs"] = new JsonObject { ["href"] = href } });
                            }
                        }
                        else
                        {
                            Warn("Removed a link with an unsupported address.");
                        }

                        break;

                    default:
                        Warn($"Removed unsupported formatting '{SafeName(type)}'.");
                        break;
                }
            }

            // The schema allows the code mark only alongside a link.
            if (seen.Contains("code"))
            {
                kept.RemoveAll(m => TypeOf(m) is "strong" or "em" or "strike");
            }

            return kept.Count == 0 ? null : new JsonArray([.. kept]);
        }

        private void WarnMarks(JsonObject node)
        {
            if (node["marks"] is not JsonArray marks)
            {
                return;
            }

            foreach (var mark in marks.OfType<JsonObject>())
            {
                Warn($"Removed unsupported formatting '{SafeName(TypeOf(mark))}'.");
            }
        }

        private List<JsonObject> TextParagraph(string text)
        {
            var taken = Take(text);
            return string.IsNullOrEmpty(taken)
                ? []
                : [new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = taken }) }];
        }

        // Spends the text budget. Once it runs out, the text is cut on a code point and marked, and the walk stops.
        private string? Take(string? text)
        {
            if (Stopped || string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (text.Length <= _budget)
            {
                _budget -= text.Length;
                return text;
            }

            var cut = AdfLimits.TruncateOnCodePoint(text, _budget) + AdfLimits.TruncatedSuffix;
            _budget = 0;
            Stopped = true;
            Warn($"Text was cut to {AdfLimits.MaxTextLength} characters.");
            return cut;
        }

        private void Warn(string message)
        {
            if (!_warnings.Contains(message))
            {
                _warnings.Add(message);
            }
        }

        private static JsonObject WithContent(JsonObject node, List<JsonObject> inline)
        {
            if (inline.Count > 0)
            {
                node["content"] = new JsonArray([.. inline]);
            }

            return node;
        }

        // The text a leaf node stands for: a mention's display text, a card's address, an emoji's short name.
        private static string LeafText(JsonObject node) =>
            ReadString(node["text"])
            ?? ReadString(node["attrs"]?["text"])
            ?? ReadString(node["attrs"]?["url"])
            ?? ReadString(node["attrs"]?["shortName"])
            ?? string.Empty;

        // All text below a node; block children are separated by newlines. Iterative with an explicit stack, so
        // hostile nesting cannot exhaust the call stack (documents built in code have no JSON depth cap).
        private static string NodeText(JsonObject root)
        {
            var builder = new StringBuilder();
            var stack = new Stack<JsonObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var type = TypeOf(node);
                if (node != root && builder.Length > 0 && BlockTypes.Contains(type) && builder[^1] != '\n')
                {
                    builder.Append('\n');
                }

                if (type == "hardBreak")
                {
                    builder.Append('\n');
                    continue;
                }

                if (node["content"] is not JsonArray children)
                {
                    builder.Append(LeafText(node));
                    continue;
                }

                foreach (var child in children.OfType<JsonObject>().Reverse())
                {
                    stack.Push(child);
                }
            }

            return builder.ToString();
        }

        // A code block keeps only its characters; hard breaks become newlines.
        private static string PlainText(JsonObject node) => NodeText(node);
    }
}
