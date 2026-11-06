using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Umbraco.Community.Automate.Jira.Adf;

/// <summary>
/// Markdown to ADF (design 7.5): paragraphs, headings 1 to 6, bullet and ordered lists, code blocks, quotes and
/// rules; strong, emphasis, strike, code and links. Anything else becomes plain text. Raw HTML is off and the
/// only extension is strikethrough, so autolinked bare domains, tables and images are never produced. Every
/// link still goes through <see cref="AdfSanitiser"/>, which decides what an allowed address is.
/// </summary>
internal sealed class MarkdownAdfConverter : IAdfConverter
{
    // Inline nesting (emphasis inside links inside emphasis…) beyond this is flattened to plain text.
    private const int MaxInlineDepth = 32;

    private static PlainTextAdfConverter PlainText => new();

    // An instance field, not static: the pipeline is a mutable object, and the converter is a singleton anyway.
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .DisableHtml()
        .Build();

    public AdfResult Convert(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new AdfResult(null, []);
        }

        // Checked before parsing, so a huge input costs no parse (F-11).
        if (Encoding.UTF8.GetByteCount(input) > AdfLimits.MaxInputBytes)
        {
            throw new AdfInputTooLargeException($"The text is larger than {AdfLimits.MaxInputBytes / 1024} KB. Shorten it or use plain text.");
        }

        MarkdownDocument markdown;
        try
        {
            markdown = Markdown.Parse(input, _pipeline);
        }
        catch (ArgumentException)
        {
            // Markdig refuses input nested past its own depth limit. Plain text keeps every character and nests nothing.
            var plain = PlainText.Convert(input);
            return plain with { Warnings = ["The Markdown was nested too deeply, so it was sent as plain text.", .. plain.Warnings] };
        }

        var doc = new JsonObject { ["type"] = "doc", ["version"] = 1, ["content"] = Blocks(markdown, depth: 1) };
        var warnings = new List<string>();
        AdfSanitiser.Sanitise(doc, warnings);
        return new AdfResult(doc, warnings);
    }

    private static JsonArray Blocks(ContainerBlock container, int depth)
    {
        var output = new JsonArray();
        foreach (var block in container)
        {
            if (depth > AdfLimits.MaxNesting)
            {
                // Deeper structure is kept as text only, gathered without recursion (F-11).
                AddParagraph(output, BlockText(block));
                continue;
            }

            switch (block)
            {
                case HeadingBlock heading:
                    output.Add(WithInline(new JsonObject { ["type"] = "heading", ["attrs"] = new JsonObject { ["level"] = Math.Clamp(heading.Level, 1, 6) } }, heading.Inline));
                    break;

                case ParagraphBlock paragraph:
                    output.Add(WithInline(new JsonObject { ["type"] = "paragraph" }, paragraph.Inline));
                    break;

                case ListBlock list:
                {
                    var node = new JsonObject { ["type"] = list.IsOrdered ? "orderedList" : "bulletList" };
                    if (list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.None, CultureInfo.InvariantCulture, out var start))
                    {
                        node["attrs"] = new JsonObject { ["order"] = start };
                    }

                    var items = new JsonArray();
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        items.Add(new JsonObject { ["type"] = "listItem", ["content"] = Blocks(item, depth + 1) });
                    }

                    node["content"] = items;
                    output.Add(node);
                    break;
                }

                case CodeBlock code:
                {
                    var node = new JsonObject { ["type"] = "codeBlock" };
                    if (code is FencedCodeBlock { Info: { Length: > 0 } info })
                    {
                        node["attrs"] = new JsonObject { ["language"] = info };
                    }

                    var text = code.Lines.ToString();
                    if (text.Length > 0)
                    {
                        node["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
                    }

                    output.Add(node);
                    break;
                }

                case QuoteBlock quote:
                    output.Add(new JsonObject { ["type"] = "blockquote", ["content"] = Blocks(quote, depth + 1) });
                    break;

                case ThematicBreakBlock:
                    output.Add(new JsonObject { ["type"] = "rule" });
                    break;

                case LinkReferenceDefinitionGroup:
                case LinkReferenceDefinition:
                    // Definitions are resolved into the links that use them and show nothing themselves.
                    break;

                case ContainerBlock other:
                    foreach (var child in Blocks(other, depth + 1).ToList())
                    {
                        output.Add(child!.DeepClone());
                    }

                    break;

                default:
                    AddParagraph(output, BlockText(block));
                    break;
            }
        }

        return output;
    }

    private static JsonObject WithInline(JsonObject node, ContainerInline? inline)
    {
        var content = new JsonArray();
        if (inline is not null)
        {
            Walk(inline, [], content, depth: 0);
        }

        if (content.Count > 0)
        {
            node["content"] = content;
        }

        return node;
    }

    private static void Walk(ContainerInline container, IReadOnlyList<JsonObject> marks, JsonArray output, int depth)
    {
        foreach (var inline in container)
        {
            if (depth > MaxInlineDepth)
            {
                AddText(output, InlineText(inline), marks);
                continue;
            }

            switch (inline)
            {
                case LiteralInline literal:
                    AddText(output, literal.Content.ToString(), marks);
                    break;

                case CodeInline code:
                    // The ADF schema allows the code mark only alongside a link.
                    AddText(output, code.Content, [.. marks.Where(m => (string?)m["type"] == "link"), Mark("code")]);
                    break;

                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                    {
                        output.Add(new JsonObject { ["type"] = "hardBreak" });
                    }
                    else
                    {
                        AddText(output, " ", marks);
                    }

                    break;

                case EmphasisInline emphasis:
                    Walk(emphasis, [.. marks, Mark(emphasis.DelimiterChar == '~' ? "strike" : emphasis.DelimiterCount >= 2 ? "strong" : "em")], output, depth + 1);
                    break;

                case LinkInline { IsImage: true } image:
                    // Images are never embedded; their alt text is kept.
                    Walk(image, marks, output, depth + 1);
                    break;

                case LinkInline link:
                    // The title is dropped; the address is checked by the sanitiser.
                    Walk(link, string.IsNullOrEmpty(link.Url) ? marks : [.. marks, LinkMark(link.Url)], output, depth + 1);
                    break;

                case AutolinkInline autolink:
                    AddText(output, autolink.Url, [.. marks, LinkMark(autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url)]);
                    break;

                case HtmlEntityInline entity:
                    AddText(output, entity.Transcoded.ToString(), marks);
                    break;

                case HtmlInline html:
                    AddText(output, html.Tag, marks);
                    break;

                case ContainerInline other:
                    Walk(other, marks, output, depth + 1);
                    break;
            }
        }
    }

    private static JsonObject Mark(string type) => new() { ["type"] = type };

    private static JsonObject LinkMark(string href) => new() { ["type"] = "link", ["attrs"] = new JsonObject { ["href"] = href } };

    private static void AddText(JsonArray output, string text, IReadOnlyList<JsonObject> marks)
    {
        if (text.Length == 0)
        {
            return;
        }

        var markArray = marks.Count == 0 ? null : new JsonArray([.. marks.Select(m => m.DeepClone())]);

        // Join with the previous text node when the marks match, so soft breaks do not fragment a paragraph.
        if (output.Count > 0 && output[^1] is JsonObject previous && (string?)previous["type"] == "text" && JsonNode.DeepEquals(previous["marks"], markArray))
        {
            previous["text"] = (string?)previous["text"] + text;
            return;
        }

        var node = new JsonObject { ["type"] = "text", ["text"] = text };
        if (markArray is not null)
        {
            node["marks"] = markArray;
        }

        output.Add(node);
    }

    private static void AddParagraph(JsonArray output, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            output.Add(new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) });
        }
    }

    // The text of a block and everything under it, joined by spaces. An explicit stack, never recursion.
    private static string BlockText(Block root)
    {
        var parts = new List<string>();
        var stack = new Stack<Block>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case ContainerBlock container:
                    for (var i = container.Count - 1; i >= 0; i--)
                    {
                        stack.Push(container[i]);
                    }

                    break;

                case LeafBlock { Inline: { } inline }:
                    parts.Add(InlineText(inline));
                    break;

                case LeafBlock leaf:
                    parts.Add(leaf.Lines.ToString());
                    break;
            }
        }

        return string.Join(" ", parts.Where(p => p.Length > 0));
    }

    private static string InlineText(Inline root)
    {
        var builder = new StringBuilder();
        var stack = new Stack<Inline>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var inline = stack.Pop();
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case AutolinkInline autolink:
                    builder.Append(autolink.Url);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case HtmlEntityInline entity:
                    builder.Append(entity.Transcoded.ToString());
                    break;
                case HtmlInline html:
                    builder.Append(html.Tag);
                    break;
                case ContainerInline container:
                    foreach (var child in container.Reverse())
                    {
                        stack.Push(child);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
