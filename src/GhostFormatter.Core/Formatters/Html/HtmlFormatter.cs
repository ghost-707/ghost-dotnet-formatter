using System.Text;
using System.Text.RegularExpressions;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Enums;
using GhostFormatter.Core.Formatters;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Abstractions.Formatters.Html;

/// <summary>
/// Formats HTML files with proper indentation and attribute wrapping.
/// Preserves DOM semantics: inline elements are kept inline, block elements are structured.
/// </summary>
public sealed partial class HtmlFormatter : BaseFormatter
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area",
        "base",
        "br",
        "col",
        "embed",
        "hr",
        "img",
        "input",
        "link",
        "meta",
        "param",
        "source",
        "track",
        "wbr",
    };

    private static readonly HashSet<string> InlineElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "abbr",
        "b",
        "bdo",
        "br",
        "cite",
        "code",
        "dfn",
        "em",
        "i",
        "img",
        "input",
        "kbd",
        "label",
        "mark",
        "q",
        "samp",
        "small",
        "span",
        "strong",
        "sub",
        "sup",
        "time",
        "var",
    };

    private static readonly HashSet<string> PreserveWhitespaceElements = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "pre",
        "code",
        "textarea",
        "script",
        "style",
    };

    /// <inheritdoc />
    public override Language Language => Language.Html;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".html", ".htm"];

    /// <summary>Initializes a new instance of the <see cref="HtmlFormatter"/> class.</summary>
    public HtmlFormatter(ILogger<HtmlFormatter> logger)
        : base(logger) { }

    /// <inheritdoc />
    protected override Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tokens = Tokenize(text);
        var sb = new StringBuilder();
        var indentLevel = 0;
        var indent = options.UseTabs ? "\t" : new string(' ', options.IndentSize);
        var preserveStack = new Stack<string>();

        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (token.Type)
            {
                case HtmlTokenType.OpenTag:
                    if (preserveStack.Count > 0)
                    {
                        sb.Append(token.Raw);
                        if (PreserveWhitespaceElements.Contains(token.TagName!))
                        {
                            preserveStack.Push(token.TagName!);
                        }
                        break;
                    }

                    if (!InlineElements.Contains(token.TagName!))
                    {
                        sb.Append('\n');
                        sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
                    }

                    var formattedTag = FormatOpenTag(token, options, indentLevel, indent);
                    sb.Append(formattedTag);

                    if (PreserveWhitespaceElements.Contains(token.TagName!))
                    {
                        preserveStack.Push(token.TagName!);
                    }
                    else if (!token.IsSelfClosing && !VoidElements.Contains(token.TagName!))
                    {
                        indentLevel++;
                    }
                    break;

                case HtmlTokenType.CloseTag:
                    if (preserveStack.Count > 0)
                    {
                        if (
                            preserveStack
                                .Peek()
                                .Equals(token.TagName, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            preserveStack.Pop();
                        }
                        sb.Append(token.Raw);
                        break;
                    }

                    if (!InlineElements.Contains(token.TagName!))
                    {
                        indentLevel = Math.Max(0, indentLevel - 1);
                        sb.Append('\n');
                        sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
                    }
                    sb.Append(token.Raw.Trim());
                    break;

                case HtmlTokenType.Text:
                    if (preserveStack.Count > 0)
                    {
                        sb.Append(token.Raw);
                    }
                    else
                    {
                        var trimmed = token.Raw.Trim();
                        if (trimmed.Length > 0)
                        {
                            sb.Append('\n');
                            sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
                            sb.Append(trimmed);
                        }
                    }
                    break;

                case HtmlTokenType.Comment:
                    sb.Append('\n');
                    sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
                    sb.Append(token.Raw.Trim());
                    break;

                case HtmlTokenType.Doctype:
                    sb.Append(token.Raw.Trim());
                    break;
            }
        }

        var result = sb.ToString().TrimStart('\n');
        return Task.FromResult(result);
    }

    private static string FormatOpenTag(
        HtmlToken token,
        FormattingOptions options,
        int indentLevel,
        string indent
    )
    {
        // If the tag is already multiline in the source, OR exceeds threshold, format it.
        var isMultiline = token.Raw.Contains('\n');
        var exceedsThreshold = token.Attributes.Count > options.Html.WrapAttributesThreshold;

        // If it is a short tag on a single line, return it cleanly on one line
        if (!exceedsThreshold && !isMultiline)
        {
            if (token.Attributes.Count == 0)
                return $"<{token.TagName}{(token.IsSelfClosing ? " />" : ">")}";
            return $"<{token.TagName} {string.Join(" ", token.Attributes)}{(token.IsSelfClosing ? " />" : ">")}";
        }

        var sb = new StringBuilder();
        sb.Append('<').Append(token.TagName);

        var attrIndent = string.Concat(Enumerable.Repeat(indent, indentLevel + 1));

        foreach (var attr in token.Attributes)
        {
            sb.Append('\n').Append(attrIndent).Append(attr.Trim());
        }

        // Drop the closing tag to a new line and align it with the opening tag
        var baseIndent = string.Concat(Enumerable.Repeat(indent, indentLevel));
        sb.Append('\n').Append(baseIndent).Append(token.IsSelfClosing ? "/>" : ">");

        return sb.ToString();
    }

    private static List<HtmlToken> Tokenize(string html)
    {
        var tokens = new List<HtmlToken>();
        var i = 0;

        while (i < html.Length)
        {
            if (html[i] == '<')
            {
                // Comment
                if (i + 3 < html.Length && html.AsSpan(i, 4).SequenceEqual("<!--"))
                {
                    var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    if (end < 0)
                        end = html.Length - 3;
                    end += 3;
                    tokens.Add(new HtmlToken(HtmlTokenType.Comment, html[i..end]));
                    i = end;
                    continue;
                }

                // Doctype
                if (
                    i + 8 < html.Length
                    && html.AsSpan(i, 9)
                        .ToString()
                        .StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                )
                {
                    var end = html.IndexOf('>', i) + 1;
                    if (end == 0)
                        end = html.Length;
                    tokens.Add(new HtmlToken(HtmlTokenType.Doctype, html[i..end]));
                    i = end;
                    continue;
                }

                // Close tag
                if (i + 1 < html.Length && html[i + 1] == '/')
                {
                    var end = html.IndexOf('>', i) + 1;
                    if (end == 0)
                        end = html.Length;
                    var raw = html[i..end];
                    var tagName = ExtractTagName(raw[2..].TrimEnd('>').Trim());
                    tokens.Add(new HtmlToken(HtmlTokenType.CloseTag, raw) { TagName = tagName });
                    i = end;
                    continue;
                }

                // Open tag
                {
                    var end = FindTagEnd(html, i);
                    var raw = html[i..end];
                    var tagName = ExtractTagName(raw[1..]);
                    var isSelfClosing = raw.EndsWith("/>", StringComparison.Ordinal);

                    // FIX: Only extract attributes from AFTER the tag name so "input" isn't treated as an attribute
                    var innerStart = 1 + tagName.Length;
                    var innerEnd = raw.Length - (isSelfClosing ? 2 : 1);
                    var innerRaw =
                        innerStart < innerEnd
                            ? raw.Substring(innerStart, innerEnd - innerStart)
                            : "";
                    var attrs = ExtractAttributes(innerRaw);

                    tokens.Add(
                        new HtmlToken(HtmlTokenType.OpenTag, raw)
                        {
                            TagName = tagName,
                            IsSelfClosing = isSelfClosing,
                            Attributes = attrs,
                        }
                    );
                    i = end;
                    continue;
                }
            }

            // Text content
            var textEnd = html.IndexOf('<', i);
            if (textEnd < 0)
                textEnd = html.Length;
            if (textEnd > i)
            {
                tokens.Add(new HtmlToken(HtmlTokenType.Text, html[i..textEnd]));
            }
            i = textEnd;
        }

        return tokens;
    }

    private static int FindTagEnd(string html, int start)
    {
        var inString = false;
        var quote = '\0';

        for (int i = start + 1; i < html.Length; i++)
        {
            if (inString)
            {
                if (html[i] == quote)
                {
                    inString = false;
                }
            }
            else if (html[i] == '"' || html[i] == '\'')
            {
                inString = true;
                quote = html[i];
            }
            else if (html[i] == '>')
            {
                return i + 1;
            }
        }

        return html.Length;
    }

    private static string ExtractTagName(string afterBracket)
    {
        var sb = new StringBuilder();
        foreach (var c in afterBracket)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':')
            {
                sb.Append(c);
            }
            else
            {
                break;
            }
        }
        return sb.ToString();
    }

    private static List<string> ExtractAttributes(string tag)
    {
        var attrs = new List<string>();
        var match = AttributeRegex().Matches(tag);

        foreach (Match m in match)
        {
            attrs.Add(m.Value.Trim());
        }

        return attrs;
    }

    [GeneratedRegex(@"[\w\-:]+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[\w\-]+))?", RegexOptions.Compiled)]
    private static partial Regex AttributeRegex();
}

internal enum HtmlTokenType
{
    OpenTag,
    CloseTag,
    Text,
    Comment,
    Doctype,
}

internal sealed class HtmlToken
{
    public HtmlTokenType Type { get; }
    public string Raw { get; }
    public string? TagName { get; init; }
    public bool IsSelfClosing { get; init; }
    public List<string> Attributes { get; init; } = [];

    public HtmlToken(HtmlTokenType type, string raw)
    {
        Type = type;
        Raw = raw;
    }
}
