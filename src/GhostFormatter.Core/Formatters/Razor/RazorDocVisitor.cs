using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Core.Formatters.Css;
using GhostFormatter.Core.Formatters.JavaScript;
using GhostFormatter.Core.Modals;
using Microsoft.Extensions.Logging;
using static GhostFormatter.Core.Docs.Docs;

namespace GhostFormatter.Core.Formatters.Razor;

public sealed class RazorDocVisitor
{
    private readonly FormattingOptions _options;
    private readonly CssFormatter _css;
    private readonly JavaScriptFormatter _js;
    private readonly ILogger _logger;

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

    public RazorDocVisitor(
        FormattingOptions options,
        CssFormatter css,
        JavaScriptFormatter js,
        ILogger logger
    )
    {
        _options = options;
        _css = css;
        _js = js;
        _logger = logger;
    }

    /// <param name="root">Held as object — RazorSyntaxTree.Root's declared type, SyntaxNode,
    /// is internal, so it can never be named as a parameter type here.</param>
    public Doc VisitRoot(object root)
    {
        var top = RazorReflectionAccess
            .GetChildNodes(root)
            .Select(Visit)
            .Where(d => !IsEmpty(d))
            .ToList();
        return top.Count == 0 ? (Doc)"" : Join(HardLine, top);
    }

    private Doc Visit(object node)
    {
        var kindName = RazorReflectionAccess.GetKindName(node);

        return kindName switch
        {
            "MarkupElement" => VisitElement(node),
            "MarkupTagBlock" => VisitElement(node),
            "MarkupTextLiteral" => VisitText(node),
            "MarkupComment" or "MarkupCommentBlock" => FromRawText(
                RazorReflectionAccess.GetFullString(node).Trim()
            ),
            "RazorComment" or "RazorCommentBlock" => FromRawText(
                RazorReflectionAccess.GetFullString(node).Trim()
            ),
            "RazorDirective" => FromRawText(RazorReflectionAccess.GetFullString(node).Trim()),
            "CSharpStatement" or "CSharpStatementLiteral" => VisitStatement(node),
            "CSharpCodeBlock" or "CSharpCode" => VisitCodeBlock(node),
            "CSharpExplicitExpression" or "CSharpImplicitExpression" => FromRawText(
                RazorReflectionAccess.GetFullString(node).Trim()
            ),
            _ => DefaultVisit(node),
        };
    }

    private Doc DefaultVisit(object node)
    {
        _logger.LogDebug(
            "RazorDocVisitor: no dedicated visitor for kind {Kind} — emitting raw source.",
            RazorReflectionAccess.GetKindName(node)
        );
        return FromRawText(RazorReflectionAccess.GetFullString(node).TrimEnd('\n', '\r'));
    }

    private static string GetTagName(object elementNode)
    {
        var text = RazorReflectionAccess.GetFullString(elementNode).TrimStart();
        var i = 1;
        var start = i;
        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '-' or '_' or ':'))
            i++;
        return i > start ? text[start..i] : "";
    }

    private Doc VisitElement(object el)
    {
        var name = GetTagName(el);

        if (
            name.Equals("script", StringComparison.OrdinalIgnoreCase)
            || name.Equals("style", StringComparison.OrdinalIgnoreCase)
        )
        {
            return VisitVerbatimElement(el, name);
        }

        var contentChildren = RazorReflectionAccess
            .GetChildNodes(el)
            .Where(n =>
            {
                var k = RazorReflectionAccess.GetKindName(n);
                return k is not ("MarkupStartTag" or "MarkupEndTag" or "MarkupTagBlock");
            })
            .Select(Visit)
            .Where(d => !IsEmpty(d))
            .ToList();

        var fullElementText = RazorReflectionAccess.GetFullString(el);
        var openTag = RenderStartTagFromSource(fullElementText, name);
        var isVoid =
            VoidElements.Contains(name)
            || !fullElementText.Contains($"</{name}", StringComparison.OrdinalIgnoreCase);

        if (isVoid)
            return openTag;

        var closeTag = $"</{name}>";

        if (contentChildren.Count == 0)
            return Concat(openTag, closeTag);

        if (InlineElements.Contains(name))
            return Concat(openTag, Concat(contentChildren.ToArray()), closeTag);

        return Concat(
            openTag,
            Indent(HardLine, Join(HardLine, contentChildren)),
            HardLine,
            closeTag
        );
    }

    private Doc RenderStartTagFromSource(string fullElementText, string name)
    {
        var gt = FindUnquotedGt(fullElementText);
        var startTagText = gt >= 0 ? fullElementText[..(gt + 1)] : fullElementText;

        var inner = startTagText.Trim();
        var selfClosing = inner.EndsWith("/>", StringComparison.Ordinal);
        var body = inner[1..^(selfClosing ? 2 : 1)];

        var firstSpace = body.IndexOf(' ');
        if (firstSpace < 0)
            return selfClosing ? $"<{name} />" : $"<{name}>";

        var attributes = TokenizeAttributes(body[(firstSpace + 1)..]);
        if (attributes.Count == 0)
            return selfClosing ? $"<{name} />" : $"<{name}>";

        var oneLine = $"<{name} {string.Join(" ", attributes)}{(selfClosing ? " />" : ">")}";
        if (
            oneLine.Length <= _options.MaxLineLength
            && attributes.Count <= _options.Html.WrapAttributesThreshold
        )
            return oneLine;

        var attributeDocs = attributes.Select(a => (Doc)a);
        return Group(
            "<",
            name,
            Indent(Line, Join(Line, attributeDocs)),
            SoftLine,
            selfClosing ? "/>" : ">"
        );
    }

    private static int FindUnquotedGt(string s)
    {
        var inString = false;
        var quote = '\0';
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (inString)
            {
                if (c == quote)
                    inString = false;
            }
            else if (c is '"' or '\'')
            {
                inString = true;
                quote = c;
            }
            else if (c == '>')
                return i;
        }
        return -1;
    }

    private static List<string> TokenizeAttributes(string attributesPart)
    {
        var result = new List<string>();
        var i = 0;
        var n = attributesPart.Length;

        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(attributesPart[i]))
                i++;
            if (i >= n)
                break;

            var start = i;
            char? quote = null;
            while (i < n)
            {
                var c = attributesPart[i];
                if (quote is not null)
                {
                    if (c == quote)
                        quote = null;
                }
                else if (c is '"' or '\'')
                    quote = c;
                else if (char.IsWhiteSpace(c))
                    break;
                i++;
            }
            result.Add(attributesPart[start..i]);
        }
        return result;
    }

    private Doc VisitVerbatimElement(object el, string tagName)
    {
        var full = RazorReflectionAccess.GetFullString(el);
        var openEnd = FindUnquotedGt(full);
        var closeStart = full.LastIndexOf($"</{tagName}", StringComparison.OrdinalIgnoreCase);

        if (openEnd < 0 || closeStart < 0 || closeStart <= openEnd + 1)
            return FromRawText(full.TrimEnd('\n', '\r'));

        var openTag = full[..(openEnd + 1)].Trim();
        var innerText = full[(openEnd + 1)..closeStart];
        var closeTag = full[closeStart..].TrimEnd('\n', '\r').Trim();

        if (string.IsNullOrWhiteSpace(innerText))
            return Concat(FromRawText(openTag), FromRawText(closeTag));

        var formatter = tagName.Equals("style", StringComparison.OrdinalIgnoreCase)
            ? (GhostFormatter.Core.Formatters.BaseFormatter)_css
            : _js;

        string formattedInner;
        try
        {
            var request = new GhostFormatter.Abstractions.Model.FormattingRequest
            {
                Text = innerText,
                Language = formatter.Language,
                Scope = GhostFormatter.Abstractions.Enums.FormattingScope.Document,
            };
            var result = formatter
                .FormatAsync(request, _options, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            formattedInner =
                result.Success && result.FormattedText is not null
                    ? result.FormattedText
                    : innerText;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to format embedded <{Tag}> block; leaving unchanged.",
                tagName
            );
            formattedInner = innerText;
        }

        var innerLines = formattedInner.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var body =
            innerLines.Length == 0
                ? (Doc)""
                : Indent(HardLine, Join(HardLine, innerLines.Select(l => (Doc)FromRawText(l))));

        return Concat(FromRawText(openTag), body, HardLine, FromRawText(closeTag));
    }

    private Doc VisitText(object text)
    {
        var content = RazorReflectionAccess.GetFullString(text);
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var collapsed = string.Join(
            ' ',
            content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );
        return FromRawText(collapsed);
    }

    private Doc VisitCodeBlock(object block)
    {
        var children = RazorReflectionAccess
            .GetChildNodes(block)
            .Select(Visit)
            .Where(d => !IsEmpty(d))
            .ToList();
        return children.Count == 0 ? "" : Join(HardLine, children);
    }

    private Doc VisitStatement(object stmt)
    {
        var fullText = RazorReflectionAccess.GetFullString(stmt);

        var markupOrStatementChildren = RazorReflectionAccess
            .GetChildNodes(stmt)
            .Where(n =>
            {
                var k = RazorReflectionAccess.GetKindName(n);
                return k
                    is "MarkupElement"
                        or "MarkupTagBlock"
                        or "CSharpStatement"
                        or "CSharpStatementLiteral"
                        or "CSharpCodeBlock"
                        or "CSharpCode";
            })
            .Select(Visit)
            .Where(d => !IsEmpty(d))
            .ToList();

        if (markupOrStatementChildren.Count == 0)
            return FromRawText(fullText.TrimEnd('\n', '\r'));

        var openBrace = fullText.IndexOf('{');
        var header = (openBrace >= 0 ? fullText[..openBrace] : fullText).Trim();

        return Concat(
            FromRawText(header),
            HardLine,
            "{",
            Indent(HardLine, Join(HardLine, markupOrStatementChildren)),
            HardLine,
            "}"
        );
    }
}
