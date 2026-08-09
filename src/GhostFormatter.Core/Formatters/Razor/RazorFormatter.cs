using System.Text;
using System.Text.RegularExpressions;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Formatters;
using GhostFormatter.Abstractions.Model;
using GhostFormatter.Core.Formatters.Css;
using GhostFormatter.Core.Formatters.JavaScript;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.Razor;

/// <summary>
/// Formats Razor (.cshtml, .razor) files containing a mix of HTML, C#, CSS, and JavaScript.
/// Preserves Razor syntax semantics including @-expressions, code blocks, and directives.
/// </summary>
public sealed partial class RazorFormatter : BaseFormatter
{
    private readonly CssFormatter _cssFormatter;
    private readonly JavaScriptFormatter _javaScriptFormatter;

    /// <inheritdoc />
    public override Language Language => Language.Razor;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".cshtml", ".razor"];

    /// <summary>Initializes a new instance of the <see cref="RazorFormatter"/> class.</summary>
    public RazorFormatter(
        ILogger<RazorFormatter> logger,
        CssFormatter cssFormatter,
        JavaScriptFormatter javaScriptFormatter
    )
        : base(logger)
    {
        _cssFormatter = cssFormatter;
        _javaScriptFormatter = javaScriptFormatter;
    }

    /// <summary>
    /// Formats a Razor file line by line.
    /// </summary>
    /// <remarks>
    /// Indentation is driven by two combined counters:
    /// <list type="bullet">
    /// <item><description><c>tagStack</c> — HTML/Razor tags currently open (as before).</description></item>
    /// <item><description><c>codeDepth</c> — C#/Razor code braces currently open, from
    /// <c>@if</c>/<c>@foreach</c>/<c>@{</c>/<c>@code</c>/<c>@section</c>/<c>else</c>/bare
    /// <c>{</c>/<c>}</c>, etc.</description></item>
    /// </list>
    /// A line is classified purely by its first character: if it starts with <c>&lt;</c> it is
    /// markup and is routed through the tag-scanning/wrapping/tagStack logic (with indentation
    /// offset by <c>codeDepth</c>); otherwise it is "code side" content (directives, control-flow
    /// headers, statements, comments, plain text) and is emitted verbatim, re-indented at
    /// <c>tagStack.Count + codeDepth</c>, with <c>codeDepth</c> updated from the net brace count
    /// on that line.
    ///
    /// Before classification, three line-splitting passes may run, each re-feeding their output
    /// back through this same per-line pipeline on subsequent loop iterations rather than
    /// duplicating any indentation/wrapping logic:
    /// <list type="number">
    /// <item><description><b>Multi-line tag joining</b> (<see cref="JoinMultiLineTag"/>): a line
    /// that begins a tag but doesn't close it on the same physical line (hand-wrapped source with
    /// one attribute per line) is joined forward until an unquoted <c>&gt;</c> is found. Without
    /// this, each attribute line is read as unrelated "code side" content, the tag's name is
    /// never pushed onto <c>tagStack</c>, and the later closing tag's unmatched pop desyncs
    /// <c>tagStack.Count</c> for the remainder of the file.</description></item>
    /// <item><description><b>Compound markup line explosion</b>
    /// (<see cref="TryExplodeCompoundMarkupLine"/>): a line that already contains more than one
    /// complete tag (e.g. hand-authored <c>&lt;td&gt;...&lt;/td&gt;</c> or
    /// <c>&lt;a&gt;...&lt;i&gt;...&lt;/i&gt;...&lt;/a&gt;</c>) and would exceed
    /// <see cref="FormattingOptions.MaxLineLength"/> is split into one segment per tag/text-run
    /// and spliced back into the line list. Each segment then flows through the ordinary
    /// single-tag path below, picking up correct nesting and attribute wrapping "for free" — in
    /// particular, text content that lands inside a tag this way becomes an ordinary code-side
    /// line and can be wrapped by <see cref="TryWrapCodeLine"/>. An open tag immediately followed
    /// by its own matching close tag (an empty leaf element) is kept merged into one segment
    /// rather than needlessly split.</description></item>
    /// <item><description><b>Text fill-wrapping</b> (<see cref="TryFillWrapTextLine"/>): a
    /// code-side line that <see cref="TryWrapCodeLine"/> doesn't apply to and that is still too
    /// long is wrapped at existing whitespace boundaries only. <c>&lt;...&gt;</c> tag spans and
    /// <c>@...(...)</c> Razor expression spans (through balanced brackets, quote-aware) are
    /// treated as atomic so a break is never inserted where no whitespace existed in the
    /// source.</description></item>
    /// </list>
    /// Individual tag attributes that are still too long after being placed on their own line
    /// (e.g. a long Razor ternary expression as a <c>class</c>/<c>style</c> value) may be further
    /// wrapped by <see cref="TryWrapAttributeExpression"/> — see <see cref="TryWrapTag"/>.
    /// </remarks>
    protected override async Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sb = new StringBuilder();
        var indent = options.UseTabs ? "\t" : new string(' ', options.IndentSize);

        var tagStack = new List<string>();
        var codeDepth = 0;

        var inVerbatimBlock = false;
        string? verbatimTagName = null;
        string? verbatimOpenTagRaw = null;
        var verbatimIndentLevel = 0;
        List<string>? verbatimBuffer = null;

        var lines = new List<string>(text.Split('\n'));

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rawLine = lines[lineIndex];

            if (inVerbatimBlock)
            {
                if (ContainsClosingTag(rawLine, verbatimTagName!))
                {
                    await FlushVerbatimBlockAsync(
                        sb,
                        verbatimBuffer!,
                        verbatimTagName!,
                        verbatimOpenTagRaw!,
                        rawLine,
                        verbatimIndentLevel,
                        indent,
                        options,
                        cancellationToken
                    );

                    PopToMatch(tagStack, verbatimTagName!);
                    inVerbatimBlock = false;
                    verbatimTagName = null;
                    verbatimOpenTagRaw = null;
                    verbatimBuffer = null;
                    continue;
                }

                verbatimBuffer!.Add(rawLine);
                continue;
            }

            var line = rawLine.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                sb.Append('\n');
                continue;
            }

            var trimmed = line.TrimStart();
            var isMarkupLine = trimmed[0] == '<';

            // A tag hand-written across multiple physical lines (one attribute per line,
            // closing '>' on its own line) is joined into a single logical line here so it
            // flows through the same tokenizing/wrapping/tagStack path as a tag already on
            // one line. See the class-level remarks above for why this matters.
            if (isMarkupLine && LooksLikeUnterminatedOpenTag(trimmed))
            {
                var (joined, consumed, remainder) = JoinMultiLineTag(lines, lineIndex);
                trimmed = joined;

                var lastConsumedIndex = lineIndex + consumed - 1;
                if (remainder is not null)
                {
                    // Content trailed the tag's '>' on its last physical line (e.g. sibling
                    // markup on the same line) — splice it back in so it's processed as its
                    // own line on the next iteration, rather than being lost.
                    lines[lastConsumedIndex] = remainder;
                    lineIndex = lastConsumedIndex - 1;
                }
                else
                {
                    lineIndex = lastConsumedIndex;
                }
            }

            // A single physical line that already contains multiple complete tags (e.g.
            // hand-authored "<td>...</td>" or "<a ...><i></i></a>") is exploded into one
            // segment per tag/text-run when the flattened line would exceed MaxLineLength.
            // The check uses tagStack.Count + codeDepth as an indentation estimate purely to
            // decide whether to bother — a line that already fits is left exactly as-is (see
            // the existing "multi-token line" fallback further down).
            if (isMarkupLine)
            {
                var estimatedIndentLevel = tagStack.Count + codeDepth;
                var estimatedBaseIndent = string.Concat(
                    Enumerable.Repeat(indent, estimatedIndentLevel)
                );
                var wouldBeTooLong =
                    estimatedBaseIndent.Length + trimmed.Length > options.MaxLineLength;

                if (wouldBeTooLong)
                {
                    var previewTokens = ScanTags(trimmed);
                    if (previewTokens.Count > 1)
                    {
                        var segments = TryExplodeCompoundMarkupLine(trimmed);
                        if (segments is not null)
                        {
                            lines.RemoveAt(lineIndex);
                            lines.InsertRange(lineIndex, segments);
                            lineIndex--;
                            continue;
                        }
                    }
                }
            }

            if (!isMarkupLine)
            {
                // Dedent for however many closing braces the line *starts* with (handles
                // "}", "} else {", and even "}}"), matching Allman-style bodies.
                var leadingCloses = 0;
                while (leadingCloses < trimmed.Length && trimmed[leadingCloses] == '}')
                {
                    leadingCloses++;
                }

                var codeIndentLevel = Math.Max(0, tagStack.Count + codeDepth - leadingCloses);

                var wrappedCode = TryWrapCodeLine(trimmed, codeIndentLevel, indent, options);
                if (wrappedCode is not null)
                {
                    sb.Append(wrappedCode);
                }
                else
                {
                    var wrappedText = TryFillWrapTextLine(
                        trimmed,
                        codeIndentLevel,
                        indent,
                        options
                    );
                    if (wrappedText is not null)
                    {
                        sb.Append(wrappedText);
                    }
                    else
                    {
                        sb.Append(string.Concat(Enumerable.Repeat(indent, codeIndentLevel)));
                        sb.Append(trimmed);
                    }
                }
                sb.Append('\n');

                codeDepth = Math.Max(0, codeDepth + NetBraceChange(trimmed));
                continue;
            }

            var tokens = ScanTags(trimmed);

            var displayLevel = tagStack.Count + codeDepth;
            if (
                tokens.Count > 0
                && tokens[0].IsClosing
                && trimmed.StartsWith("</", StringComparison.Ordinal)
            )
            {
                displayLevel = Math.Max(0, displayLevel - 1);
            }

            // CHANGED: void/self-closing single tags (e.g. <input ...>) are now allowed to
            // wrap too — they were previously excluded here, which meant an attribute-heavy
            // <input> could never be wrapped no matter how long it was.
            var wrapped =
                tokens.Count == 1 && !tokens[0].IsClosing
                    ? TryWrapTag(trimmed, displayLevel, indent, options)
                    : null;

            if (wrapped is not null)
            {
                sb.Append(wrapped);
            }
            else
            {
                sb.Append(string.Concat(Enumerable.Repeat(indent, displayLevel)));
                sb.Append(trimmed);
            }
            sb.Append('\n');

            foreach (var token in tokens)
            {
                if (token.IsClosing)
                {
                    PopToMatch(tagStack, token.Name);
                }
                else if (!token.IsVoidOrSelfClosing)
                {
                    tagStack.Add(token.Name);

                    var isVerbatimTag =
                        token.Name.Equals("script", StringComparison.OrdinalIgnoreCase)
                        || token.Name.Equals("style", StringComparison.OrdinalIgnoreCase);

                    if (isVerbatimTag)
                    {
                        if (!ContainsClosingTag(trimmed, token.Name))
                        {
                            inVerbatimBlock = true;
                            verbatimTagName = token.Name;
                            verbatimOpenTagRaw = trimmed;
                            verbatimIndentLevel = displayLevel;
                            verbatimBuffer = [];
                        }
                        else
                        {
                            PopToMatch(tagStack, token.Name);
                        }
                    }
                }
            }
        }

        return sb.ToString().TrimEnd('\n');
    }

    private async Task FlushVerbatimBlockAsync(
        StringBuilder sb,
        List<string> bufferedLines,
        string tagName,
        string openTagRaw,
        string closingTagLine,
        int indentLevel,
        string indent,
        FormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        var innerText = string.Join('\n', bufferedLines);
        var contentIndent = string.Concat(Enumerable.Repeat(indent, indentLevel + 1));

        var shouldFormat = tagName.Equals("style", StringComparison.OrdinalIgnoreCase)
            ? true
            : IsJavaScriptScriptTag(openTagRaw);

        string? formattedInner = null;

        if (shouldFormat && !string.IsNullOrWhiteSpace(innerText))
        {
            try
            {
                var formatter = tagName.Equals("style", StringComparison.OrdinalIgnoreCase)
                    ? (ILanguageFormatter)_cssFormatter
                    : _javaScriptFormatter;

                var request = new FormattingRequest
                {
                    Text = innerText,
                    Language = formatter.Language,
                    Scope = FormattingScope.Document,
                };

                var result = await formatter.FormatAsync(request, options, cancellationToken);
                if (result.Success && result.FormattedText is not null)
                {
                    formattedInner = result.FormattedText;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    ex,
                    "Failed to format embedded <{Tag}> block; leaving it unchanged.",
                    tagName
                );
            }
        }

        var innerToEmit = formattedInner ?? innerText;

        var innerLines = innerToEmit.Split('\n');

        // Find the minimum indentation across all non-blank lines — that's the
        // CSS/JS formatter's own "base" indent (typically 0). We strip only that
        // much, preserving the relative structure (e.g. properties indented under
        // their selector), then prepend our own contentIndent so the whole block
        // sits at the right depth inside the Razor file.
        var minIndent = int.MaxValue;
        foreach (var innerLine in innerLines)
        {
            if (string.IsNullOrWhiteSpace(innerLine))
                continue;
            var leading = innerLine.Length - innerLine.TrimStart().Length;
            if (leading < minIndent)
                minIndent = leading;
        }
        if (minIndent == int.MaxValue)
            minIndent = 0;

        foreach (var innerLine in innerLines)
        {
            if (string.IsNullOrWhiteSpace(innerLine))
            {
                sb.Append('\n');
                continue;
            }

            // Strip only the common base indent, not all leading whitespace
            var stripped =
                innerLine.Length > minIndent ? innerLine[minIndent..] : innerLine.TrimStart();

            sb.Append(contentIndent);
            sb.Append(stripped);
            sb.Append('\n');
        }

        sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
        sb.Append(closingTagLine.Trim());
        sb.Append('\n');
    }

    private static bool IsJavaScriptScriptTag(string openTagRaw)
    {
        var match = ScriptTypeAttributeRegex().Match(openTagRaw);
        if (!match.Success)
        {
            return true;
        }

        var type = match.Groups["type"].Value.Trim().ToLowerInvariant();
        return type is "" or "text/javascript" or "application/javascript" or "module";
    }

    private readonly record struct TagToken(bool IsClosing, string Name, bool IsVoidOrSelfClosing);

    private static List<TagToken> ScanTags(string line)
    {
        var tokens = new List<TagToken>();
        foreach (Match m in TagTokenRegex().Matches(line))
        {
            var name = m.Groups["name"].Value;
            var isClosing = m.Groups["close"].Success;
            var isVoidOrSelfClosing = m.Groups["selfclose"].Success || VoidElements.Contains(name);
            tokens.Add(new TagToken(isClosing, name, isVoidOrSelfClosing));
        }
        return tokens;
    }

    private static void PopToMatch(List<string> stack, string tagName)
    {
        var idx = stack.FindLastIndex(n =>
            string.Equals(n, tagName, StringComparison.OrdinalIgnoreCase)
        );
        if (idx >= 0)
        {
            stack.RemoveRange(idx, stack.Count - idx);
        }
    }

    private static bool ContainsClosingTag(string line, string tagName) =>
        Regex.IsMatch(line, $@"</{Regex.Escape(tagName)}\s*>", RegexOptions.IgnoreCase);

    /// <summary>
    /// Splits a single physical markup line that contains more than one complete tag (per
    /// <see cref="TagTokenRegex"/>) into one segment per tag, plus a segment for any text
    /// between tags. An open tag immediately followed by its own matching close tag with
    /// nothing between (an empty leaf element, e.g. <c>&lt;i&gt;&lt;/i&gt;</c>) is merged back
    /// into a single segment so it isn't needlessly split across two lines. Returns null if the
    /// line doesn't contain at least two tags, or if merging collapses everything back down to
    /// one segment (nothing left to usefully split).
    /// </summary>
    private static List<string>? TryExplodeCompoundMarkupLine(string trimmedLine)
    {
        var matches = TagTokenRegex().Matches(trimmedLine);
        if (matches.Count < 2)
        {
            return null;
        }

        var rawSegments = new List<(string Text, bool IsTag, string? TagName, bool IsClosing)>();
        var cursor = 0;

        foreach (Match m in matches)
        {
            if (m.Index > cursor)
            {
                var text = trimmedLine[cursor..m.Index].Trim();
                if (text.Length > 0)
                {
                    rawSegments.Add((text, false, null, false));
                }
            }

            var tagName = m.Groups["name"].Value;
            var isClosing = m.Groups["close"].Success;
            rawSegments.Add((m.Value, true, tagName, isClosing));
            cursor = m.Index + m.Length;
        }

        if (cursor < trimmedLine.Length)
        {
            var trailing = trimmedLine[cursor..].Trim();
            if (trailing.Length > 0)
            {
                rawSegments.Add((trailing, false, null, false));
            }
        }

        if (rawSegments.Count < 2)
        {
            return null;
        }

        var merged = new List<string>();
        for (var i = 0; i < rawSegments.Count; i++)
        {
            var (text, isTag, tagName, isClosing) = rawSegments[i];

            if (
                isTag
                && !isClosing
                && i + 1 < rawSegments.Count
                && rawSegments[i + 1].IsTag
                && rawSegments[i + 1].IsClosing
                && string.Equals(
                    rawSegments[i + 1].TagName,
                    tagName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                merged.Add(text + rawSegments[i + 1].Text);
                i++;
                continue;
            }

            merged.Add(text);
        }

        return merged.Count > 1 ? merged : null;
    }

    /// <summary>
    /// True when <paramref name="trimmedLine"/> looks like the start of an HTML/Razor open tag
    /// (a <c>&lt;</c> immediately followed by a letter — a genuine tag name, not stray text like
    /// <c>&lt; 5 items</c>) that has no unquoted <c>&gt;</c> anywhere on it, meaning the tag must
    /// continue on subsequent physical lines.
    /// </summary>
    private static bool LooksLikeUnterminatedOpenTag(string trimmedLine)
    {
        if (!IsHtmlOpenTag(trimmedLine))
        {
            return false;
        }
        if (trimmedLine.Length < 2 || !char.IsLetter(trimmedLine[1]))
        {
            return false;
        }
        return FindUnquotedGt(trimmedLine) < 0;
    }

    /// <summary>
    /// Starting at <paramref name="startIndex"/>, space-joins physical lines from
    /// <paramref name="lines"/> until an unquoted <c>&gt;</c> is found, producing a single
    /// logical tag line. If content trails the <c>&gt;</c> on the line where it was found (e.g.
    /// sibling markup on the same physical line), that trailing content is returned separately
    /// as <c>Remainder</c> so the caller can splice it back in as its own line rather than
    /// losing it. Quote-aware, so a <c>&gt;</c> inside an attribute value's quotes is ignored.
    /// </summary>
    private static (string Joined, int LinesConsumed, string? Remainder) JoinMultiLineTag(
        List<string> lines,
        int startIndex
    )
    {
        var sb = new StringBuilder();
        var consumed = 0;
        string? remainder = null;

        for (var i = startIndex; i < lines.Count; i++)
        {
            var piece = lines[i].Trim();
            if (sb.Length > 0 && piece.Length > 0)
            {
                sb.Append(' ');
            }
            sb.Append(piece);
            consumed++;

            var gtIndex = FindUnquotedGt(sb.ToString());
            if (gtIndex < 0)
            {
                continue;
            }

            var full = sb.ToString();
            var after = full[(gtIndex + 1)..].TrimStart();
            if (after.Length > 0)
            {
                remainder = after;
            }
            sb.Length = gtIndex + 1;
            break;
        }

        return (sb.ToString(), consumed, remainder);
    }

    /// <summary>
    /// Finds the first <c>&gt;</c> in <paramref name="s"/> that is not inside a single- or
    /// double-quoted attribute value, or -1 if none exists.
    /// </summary>
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
                {
                    inString = false;
                }
            }
            else if (c is '"' or '\'')
            {
                inString = true;
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Computes the net change in open-brace depth for a single line of C#/Razor code,
    /// ignoring braces that appear inside double- or single-quoted literals (so a line like
    /// <c>if (c == '{') braceDepth++;</c> doesn't miscount). This intentionally does not
    /// understand verbatim (<c>@"..."</c>) or raw string literals — an acceptable gap for a
    /// line-based formatter, since such literals containing braces are rare in practice.
    /// </summary>
    private static int NetBraceChange(string line)
    {
        var net = 0;
        var i = 0;

        while (i < line.Length)
        {
            var c = line[i];

            if (c is '"' or '\'')
            {
                i = SkipQuoted(line, i);
                continue;
            }

            if (c == '{')
            {
                net++;
            }
            else if (c == '}')
            {
                net--;
            }

            i++;
        }

        return net;
    }

    /// <summary>
    /// Wraps a code-side line (a plain call or statement — not a block header) that exceeds
    /// <see cref="FormattingOptions.MaxLineLength"/>, by splitting the first bracketed
    /// argument list onto its own indented lines at top-level commas. Recurses into any
    /// individual argument that is itself still too long (e.g. an anonymous object
    /// initializer), one indent level deeper. Only triggers when the line (after stripping
    /// a trailing <c>;</c>) ends in <c>)</c>, so block headers like <c>@if (...) {</c> or
    /// <c>@code {</c> — which end in <c>{</c> — are left untouched.
    /// </summary>
    private static string? TryWrapCodeLine(
        string trimmedLine,
        int indentLevel,
        string indentUnit,
        FormattingOptions options
    )
    {
        var baseIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel));
        if (baseIndent.Length + trimmedLine.Length <= options.MaxLineLength)
        {
            return null;
        }

        var trailingSemicolon = trimmedLine.EndsWith(';');
        var body = trailingSemicolon ? trimmedLine[..^1] : trimmedLine;

        if (!body.EndsWith(')'))
        {
            return null;
        }

        var wrapped = WrapAtOutermostBracket(body, indentLevel, indentUnit, options);
        if (wrapped is null)
        {
            return null;
        }

        return trailingSemicolon ? wrapped + ";" : wrapped;
    }

    /// <summary>
    /// Finds the first unquoted bracket in <paramref name="expr"/>, splits its contents at
    /// top-level commas (commas not nested inside a further bracket or quote), and emits one
    /// argument per line. Each argument that is still too long for
    /// <see cref="FormattingOptions.MaxLineLength"/> is recursively wrapped the same way, one
    /// indent level deeper. Returns null if there's no bracket, or nothing inside it, to wrap.
    /// </summary>
    private static string? WrapAtOutermostBracket(
        string expr,
        int indentLevel,
        string indentUnit,
        FormattingOptions options
    )
    {
        var openIndex = FindFirstUnquotedBracket(expr);
        if (openIndex < 0)
        {
            return null;
        }

        var closeIndex = FindMatchingClose(expr, openIndex);
        if (closeIndex < 0 || closeIndex <= openIndex + 1)
        {
            return null;
        }

        var prefix = expr[..(openIndex + 1)];
        var suffix = expr[closeIndex..];
        var inner = expr[(openIndex + 1)..closeIndex];

        if (string.IsNullOrWhiteSpace(inner))
        {
            return null;
        }

        var parts = SplitTopLevel(inner, ',');

        var baseIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel));
        var argIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel + 1));

        var sb = new StringBuilder();
        sb.Append(baseIndent).Append(prefix.TrimEnd());

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i].Trim();
            var isLast = i == parts.Count - 1;
            var trailer = isLast ? "" : ",";

            var flatLine = argIndent + part + trailer;
            if (flatLine.Length > options.MaxLineLength)
            {
                var nested = WrapAtOutermostBracket(part, indentLevel + 1, indentUnit, options);
                if (nested is not null)
                {
                    sb.Append('\n').Append(nested).Append(trailer);
                    continue;
                }
            }

            sb.Append('\n').Append(argIndent).Append(part).Append(trailer);
        }

        sb.Append('\n').Append(baseIndent).Append(suffix.TrimStart());
        return sb.ToString();
    }

    private static int FindFirstUnquotedBracket(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c is '"' or '\'')
            {
                i = SkipQuoted(s, i);
                continue;
            }
            if (c is '(' or '{' or '[')
            {
                return i;
            }
            i++;
        }
        return -1;
    }

    private static int FindMatchingClose(string s, int openIndex)
    {
        var depth = 0;
        var i = openIndex;
        while (i < s.Length)
        {
            var c = s[i];
            if (c is '"' or '\'')
            {
                i = SkipQuoted(s, i);
                continue;
            }
            if (c is '(' or '{' or '[')
            {
                depth++;
            }
            else if (c is ')' or '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
            i++;
        }
        return -1;
    }

    /// <summary>
    /// Splits <paramref name="s"/> on <paramref name="separator"/> at nesting depth 0 only —
    /// separators inside a further <c>()</c>/<c>{}</c>/<c>[]</c> or inside a quoted literal are
    /// left alone, so e.g. an anonymous object argument's internal commas don't get split at
    /// the wrong level.
    /// </summary>
    private static List<string> SplitTopLevel(string s, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        var i = 0;

        while (i < s.Length)
        {
            var c = s[i];
            if (c is '"' or '\'')
            {
                i = SkipQuoted(s, i);
                continue;
            }
            if (c is '(' or '{' or '[')
            {
                depth++;
            }
            else if (c is ')' or '}' or ']')
            {
                depth--;
            }
            else if (c == separator && depth == 0)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
            i++;
        }

        parts.Add(s[start..]);
        return parts;
    }

    private static int SkipQuoted(string s, int quoteIndex)
    {
        var quote = s[quoteIndex];
        var i = quoteIndex + 1;
        while (i < s.Length)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                i += 2;
                continue;
            }
            if (s[i] == quote)
            {
                return i + 1;
            }
            i++;
        }
        return s.Length;
    }

    /// <summary>
    /// Tokenizes plain text (possibly containing embedded HTML tags and Razor expressions) into
    /// atomic, safely-breakable "words" for <see cref="TryFillWrapTextLine"/>. A word is a
    /// maximal run of non-whitespace characters, where:
    /// <list type="bullet">
    /// <item><description>a quoted span (<c>"..."</c> / <c>'...'</c>) is consumed whole, so a
    /// space inside a quoted attribute value never becomes a break point;</description></item>
    /// <item><description>an HTML tag (<c>&lt;...&gt;</c>) is consumed whole via
    /// <see cref="FindUnquotedGt"/>, so its internal attribute spacing never becomes a break
    /// point;</description></item>
    /// <item><description>a Razor expression (<c>@identifier...</c> or <c>@(...)</c>) is
    /// consumed whole via <see cref="ConsumeRazorExpressionSpan"/>, including any balanced
    /// <c>()</c>/<c>[]</c> that directly follow, so its arguments never become a break
    /// point.</description></item>
    /// </list>
    /// The run only ends at an actual whitespace character from the source, so no break is ever
    /// inserted at a position that wasn't already whitespace.
    /// </summary>
    private static List<string> TokenizeTextWords(string s)
    {
        var words = new List<string>();
        var i = 0;
        var n = s.Length;

        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
            if (i >= n)
            {
                break;
            }

            var start = i;
            while (i < n && !char.IsWhiteSpace(s[i]))
            {
                if (s[i] is '"' or '\'')
                {
                    i = SkipQuoted(s, i);
                    continue;
                }
                if (s[i] == '<')
                {
                    var relGt = FindUnquotedGt(s[i..]);
                    i = relGt >= 0 ? i + relGt + 1 : n;
                    continue;
                }
                if (s[i] == '@' && i + 1 < n && (char.IsLetter(s[i + 1]) || s[i + 1] == '('))
                {
                    i = ConsumeRazorExpressionSpan(s, i);
                    continue;
                }
                i++;
            }

            words.Add(s[start..i]);
        }

        return words;
    }

    /// <summary>
    /// Consumes a Razor expression starting at the <c>@</c> in <paramref name="s"/> at
    /// <paramref name="start"/> — through identifier/member-access characters, and through any
    /// balanced <c>()</c>/<c>[]</c> that directly follow (so a call's or indexer's arguments,
    /// including any internal whitespace, are swallowed as part of the same atomic span rather
    /// than becoming a break point). Quote-aware via <see cref="FindMatchingClose"/>. Used so
    /// text fill-wrapping never inserts a line break inside a Razor expression.
    /// </summary>
    private static int ConsumeRazorExpressionSpan(string s, int start)
    {
        var i = start + 1;

        while (i < s.Length)
        {
            var c = s[i];
            if (c is '(' or '[')
            {
                var close = FindMatchingClose(s, i);
                if (close < 0)
                {
                    return s.Length;
                }
                i = close + 1;
                continue;
            }
            if (char.IsLetterOrDigit(c) || c is '.' or '_')
            {
                i++;
                continue;
            }
            break;
        }

        return i;
    }

    /// <summary>
    /// Fill-wraps a code-side line that <see cref="TryWrapCodeLine"/> doesn't apply to and that
    /// still exceeds <see cref="FormattingOptions.MaxLineLength"/>, breaking only at existing
    /// whitespace boundaries per <see cref="TokenizeTextWords"/> (never inside a quoted span, an
    /// HTML tag, or a Razor expression). Returns null if the line already fits, or if it
    /// tokenizes to fewer than two words (nothing safe to break).
    /// </summary>
    private static string? TryFillWrapTextLine(
        string trimmedLine,
        int indentLevel,
        string indentUnit,
        FormattingOptions options
    )
    {
        var baseIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel));
        if (baseIndent.Length + trimmedLine.Length <= options.MaxLineLength)
        {
            return null;
        }

        var words = TokenizeTextWords(trimmedLine);
        if (words.Count < 2)
        {
            return null;
        }

        var outputLines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            var candidateLength = baseIndent.Length + current.Length + 1 + word.Length;
            if (candidateLength <= options.MaxLineLength)
            {
                current.Append(' ').Append(word);
            }
            else
            {
                outputLines.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
        }

        if (current.Length > 0)
        {
            outputLines.Add(current.ToString());
        }

        if (outputLines.Count < 2)
        {
            return null;
        }

        return string.Join('\n', outputLines.Select(l => baseIndent + l));
    }

    /// <summary>
    /// Wraps an HTML/Razor tag across multiple lines (one attribute per line) when it
    /// exceeds <see cref="FormattingOptions.MaxLineLength"/> or has more attributes than
    /// <see cref="HtmlFormattingOptions.WrapAttributesThreshold"/>. Any individual attribute
    /// that is still too long once placed on its own line is given a further chance to wrap
    /// via <see cref="TryWrapAttributeExpression"/> (currently: attributes whose value is a
    /// pure <c>@(...)</c> Razor ternary expression); other still-too-long attributes are left
    /// as a single (long) line rather than risk corrupting their value.
    /// </summary>
    private static string? TryWrapTag(
        string trimmedLine,
        int indentLevel,
        string indentUnit,
        FormattingOptions options
    )
    {
        if (!IsHtmlOpenTag(trimmedLine) || !trimmedLine.EndsWith('>'))
        {
            return null;
        }

        var sourceSelfClosing = trimmedLine.EndsWith("/>", StringComparison.Ordinal);
        var inner = trimmedLine[1..^(sourceSelfClosing ? 2 : 1)];

        var firstSpace = -1;
        for (var i = 0; i < inner.Length; i++)
        {
            if (char.IsWhiteSpace(inner[i]))
            {
                firstSpace = i;
                break;
            }
        }

        if (firstSpace < 0)
        {
            return null;
        }

        var tagName = inner[..firstSpace];
        var attributes = TokenizeAttributes(inner[firstSpace..]);
        if (attributes.Count == 0)
        {
            return null;
        }

        var baseIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel));
        var effectiveLineLength = baseIndent.Length + trimmedLine.Length;

        var tooLong = effectiveLineLength > options.MaxLineLength;
        var tooManyAttrs = attributes.Count > options.Html.WrapAttributesThreshold;
        if (!tooLong && !tooManyAttrs)
        {
            return null;
        }

        var emitSelfClosing = sourceSelfClosing || VoidElements.Contains(tagName);

        var attrIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel + 1));

        var sb = new StringBuilder();
        sb.Append(baseIndent).Append('<').Append(tagName);
        foreach (var attribute in attributes)
        {
            var flatAttrLine = attrIndent + attribute;
            if (flatAttrLine.Length > options.MaxLineLength)
            {
                var wrappedAttr = TryWrapAttributeExpression(
                    attribute,
                    indentLevel + 1,
                    indentUnit
                );
                if (wrappedAttr is not null)
                {
                    sb.Append('\n').Append(wrappedAttr);
                    continue;
                }
            }

            sb.Append('\n').Append(attrIndent).Append(attribute);
        }
        sb.Append('\n').Append(baseIndent).Append(emitSelfClosing ? "/>" : ">");
        return sb.ToString();
    }

    /// <summary>
    /// Attempts to wrap a single HTML/Razor attribute whose value is a pure, fully-parenthesized
    /// Razor expression — <c>name="@(...)"</c>, with nothing before <c>@(</c> or after the
    /// matching <c>)</c> inside the quotes — across multiple lines. Because the entire attribute
    /// value is one C# expression, and C# is whitespace-insensitive inside an expression,
    /// inserting newlines here cannot change what the expression evaluates to; it only changes
    /// how the source reads.
    /// Currently only handles a single top-level ternary (<c>condition ? a : b</c>) inside the
    /// expression, via <see cref="FindTopLevelTernary"/> — the common pattern for conditional
    /// <c>class</c>/<c>style</c> attributes. Other long expressions (a bare method call with long
    /// arguments, an expression with no ternary, or a nested ternary) are left unwrapped; returns
    /// null in that case so the caller falls back to emitting the attribute on one line.
    /// </summary>
    private static string? TryWrapAttributeExpression(
        string attribute,
        int indentLevel,
        string indentUnit
    )
    {
        var parsed = ParseQuotedAttribute(attribute);
        if (parsed is null)
        {
            return null;
        }

        var (name, quote, rawValue) = parsed.Value;
        var inner = ExtractFullRazorParenExpression(rawValue);
        if (inner is null)
        {
            return null;
        }

        var ternary = FindTopLevelTernary(inner);
        if (ternary is null)
        {
            return null;
        }

        var (questionIndex, colonIndex) = ternary.Value;
        var condition = inner[..questionIndex].Trim();
        var whenTrue = inner[(questionIndex + 1)..colonIndex].Trim();
        var whenFalse = inner[(colonIndex + 1)..].Trim();

        var baseIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel));
        var innerIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel + 1));

        var sb = new StringBuilder();
        sb.Append(baseIndent).Append(name).Append('=').Append(quote).Append("@(");
        sb.Append('\n').Append(innerIndent).Append(condition);
        sb.Append('\n').Append(innerIndent).Append("? ").Append(whenTrue);
        sb.Append('\n').Append(innerIndent).Append(": ").Append(whenFalse);
        sb.Append('\n').Append(baseIndent).Append(')').Append(quote);
        return sb.ToString();
    }

    /// <summary>
    /// Splits a <c>name="value"</c> / <c>name='value'</c> attribute token into its name, quote
    /// character, and value (with the surrounding quotes removed). Returns null for value-less
    /// attributes (e.g. <c>disabled</c>) or anything that doesn't cleanly match that shape.
    /// </summary>
    private static (string Name, char Quote, string RawValue)? ParseQuotedAttribute(
        string attribute
    )
    {
        var eq = attribute.IndexOf('=');
        if (eq < 0 || eq + 2 >= attribute.Length)
        {
            return null;
        }

        var quote = attribute[eq + 1];
        if (quote is not ('"' or '\''))
        {
            return null;
        }

        if (attribute[^1] != quote)
        {
            return null;
        }

        var name = attribute[..eq];
        var value = attribute[(eq + 2)..^1];
        return (name, quote, value);
    }

    /// <summary>
    /// Returns the C# expression text inside <c>@(...)</c> only when that expression is the
    /// <em>entire</em> attribute value — i.e. <paramref name="value"/> starts with <c>@(</c> and
    /// the matching close paren is the very last character. If any literal text sits before
    /// <c>@(</c> or after the matching <c>)</c> (e.g. <c>"prefix @(...) suffix"</c>), returns
    /// null, since wrapping would then interleave HTML-literal text with code, which is out of
    /// scope here.
    /// </summary>
    private static string? ExtractFullRazorParenExpression(string value)
    {
        if (!value.StartsWith("@(", StringComparison.Ordinal) || !value.EndsWith(')'))
        {
            return null;
        }

        const int openIndex = 1;
        var closeIndex = FindMatchingClose(value, openIndex);
        if (closeIndex != value.Length - 1)
        {
            return null;
        }

        return value[(openIndex + 1)..closeIndex];
    }

    /// <summary>
    /// Finds a single top-level (depth-0, outside quotes) ternary <c>?</c> and its matching
    /// <c>:</c> in <paramref name="s"/>. Skips <c>?.</c>, <c>?[</c>, and <c>??</c>
    /// (null-conditional / null-coalescing, not ternary) when looking for the <c>?</c>, and
    /// skips <c>::</c> when looking for the <c>:</c>. Returns null if there's no top-level
    /// <c>?</c>, no following top-level <c>:</c>, or more than one top-level <c>?</c> (a nested
    /// ternary — deliberately not handled, to avoid guessing wrong about precedence).
    /// </summary>
    private static (int QuestionIndex, int ColonIndex)? FindTopLevelTernary(string s)
    {
        var depth = 0;
        var questionIndex = -1;
        var i = 0;

        while (i < s.Length)
        {
            var c = s[i];

            if (c is '"' or '\'')
            {
                i = SkipQuoted(s, i);
                continue;
            }

            if (c is '(' or '{' or '[')
            {
                depth++;
            }
            else if (c is ')' or '}' or ']')
            {
                depth--;
            }
            else if (depth == 0 && c == '?')
            {
                var next = i + 1 < s.Length ? s[i + 1] : '\0';
                if (next is '.' or '?')
                {
                    // '?.' (null-conditional) or '??' (null-coalescing) — not a ternary.
                    i += 2;
                    continue;
                }
                if (next == '[')
                {
                    // '?[' (null-conditional index access) — skip only the '?' so the
                    // following '[' is still processed normally and depth stays balanced.
                    i++;
                    continue;
                }

                if (questionIndex >= 0)
                {
                    return null;
                }

                questionIndex = i;
            }
            else if (depth == 0 && c == ':' && questionIndex >= 0)
            {
                var prev = i > 0 ? s[i - 1] : '\0';
                var next = i + 1 < s.Length ? s[i + 1] : '\0';
                if (prev == ':' || next == ':')
                {
                    i++;
                    continue;
                }

                return (questionIndex, i);
            }

            i++;
        }

        return null;
    }

    private static List<string> TokenizeAttributes(string attributesPart)
    {
        var result = new List<string>();
        var i = 0;
        var n = attributesPart.Length;

        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(attributesPart[i]))
            {
                i++;
            }

            if (i >= n)
            {
                break;
            }

            var start = i;
            char? quote = null;

            while (i < n)
            {
                var c = attributesPart[i];
                if (quote is not null)
                {
                    if (c == quote)
                    {
                        quote = null;
                    }
                }
                else if (c is '"' or '\'')
                {
                    quote = c;
                }
                else if (char.IsWhiteSpace(c))
                {
                    break;
                }

                i++;
            }

            result.Add(attributesPart[start..i]);
        }

        return result;
    }

    private static bool IsHtmlOpenTag(string line)
    {
        return line.StartsWith('<')
            && !line.StartsWith("</", StringComparison.Ordinal)
            && !line.StartsWith("<!--", StringComparison.Ordinal)
            && !line.StartsWith("<!", StringComparison.Ordinal);
    }

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

    [GeneratedRegex(@"<(?<close>/)?(?<name>[A-Za-z][\w:-]*)(?<attrs>[^<>]*?)(?<selfclose>/)?>")]
    private static partial Regex TagTokenRegex();

    [GeneratedRegex(@"type\s*=\s*[""'](?<type>[^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTypeAttributeRegex();
}
