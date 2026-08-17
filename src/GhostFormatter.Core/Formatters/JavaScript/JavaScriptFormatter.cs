using System.Text;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Enums;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.JavaScript;

/// <summary>
/// Formats JavaScript and JSX files with proper indentation and spacing.
/// Preserves ASI (Automatic Semicolon Insertion) behavior and template literal contents.
/// </summary>
/// <remarks>
/// NOTE: This formatter is also invoked on the "JS" content of a Razor &lt;script&gt; block
/// (see RazorFormatter.FlushVerbatimBlockAsync), which is captured as raw text between
/// &lt;script&gt; and &lt;/script&gt; with no awareness of Razor syntax. That raw text can
/// legally contain Razor comments (<c>@* ... *@</c>) interleaved with real JS — RazorFormatter
/// does not strip them before handing the buffer over. This formatter therefore has to treat
/// <c>@* ... *@</c> as an opaque, skippable span too, exactly like a <c>/* ... */</c> block
/// comment, or brace-counting and re-indentation silently break the moment one appears.
/// </remarks>
public sealed class JavaScriptFormatter : BaseFormatter
{
    /// <inheritdoc />
    public override Language Language => Language.JavaScript;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".js", ".mjs", ".jsx"];

    /// <summary>Initializes a new instance of the <see cref="JavaScriptFormatter"/> class.</summary>
    public JavaScriptFormatter(ILogger<JavaScriptFormatter> logger)
        : base(logger) { }

    /// <inheritdoc />
    protected override Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sb = new StringBuilder();
        var indent = options.UseTabs ? "\t" : new string(' ', options.IndentSize);
        var indentLevel = 0;
        var inTemplateLiteral = false;

        // Tracks whether we're inside a multi-line "/* ... */" block comment. Without this,
        // each physical line of a multi-line block comment was independently re-parsed as
        // ordinary code: any brace character inside comment prose (e.g. "/* uses {a} */")
        // silently shifted indentLevel, and an apostrophe in comment prose ("don't", "user's")
        // could trip the line-local quote tracker into thinking a string had opened,
        // suppressing brace counting on that line and desyncing indentLevel from then on.
        var inBlockComment = false;

        // NEW: tracks whether we're inside a multi-line Razor comment ("@* ... *@"). This
        // text is not valid JavaScript at all, but it shows up here whenever a <script> block
        // in a .cshtml file contains a Razor comment — RazorFormatter hands the raw buffer to
        // this formatter without stripping it first (see class remarks above). Without this
        // flag, a Razor comment closing mid-line (e.g. "...; *@function foo() {") left the
        // real code trailing the "*@" completely unindented and unprocessed, because nothing
        // recognized "*@" as "comment ends here, live code starts right after it".
        var inRazorComment = false;

        var lines = text.Split('\n');

        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessLine(
                rawLine,
                sb,
                indent,
                ref indentLevel,
                ref inTemplateLiteral,
                ref inBlockComment,
                ref inRazorComment
            );
        }

        var result = sb.ToString().TrimEnd('\n');
        return Task.FromResult(result);
    }

    private static void ProcessLine(
        string rawLine,
        StringBuilder sb,
        string indent,
        ref int indentLevel,
        ref bool inTemplateLiteral,
        ref bool inBlockComment,
        ref bool inRazorComment
    )
    {
        if (inTemplateLiteral)
        {
            HandleActiveTemplateLiteral(rawLine, sb, ref inTemplateLiteral);
            return;
        }

        // While inside a multi-line "/* ... */" comment, every line is emitted verbatim (no
        // re-indentation, no brace counting) until the line that contains the closing "*/".
        if (inBlockComment)
        {
            HandleActiveBlockComment(rawLine, sb, ref inBlockComment);
            return;
        }

        // NEW: same idea as inBlockComment, but for "@* ... *@" Razor comments — with one
        // important difference. A Razor comment frequently closes mid-line with real JS
        // trailing it on the very same physical line (that's exactly what's happening in the
        // user's file: "... *@function includeHTML() {"). If we just dumped the whole raw
        // line like HandleActiveBlockComment does, that trailing code would never get
        // indented or brace-counted. So HandleActiveRazorComment splits the line at "*@" and
        // re-feeds whatever comes after it back through this same method (indent, inTemplateLiteral,
        // inBlockComment, inRazorComment) so it's treated as a brand-new, ordinary line.
        if (inRazorComment)
        {
            HandleActiveRazorComment(
                rawLine,
                sb,
                indent,
                ref indentLevel,
                ref inTemplateLiteral,
                ref inBlockComment,
                ref inRazorComment
            );
            return;
        }

        var line = rawLine.Trim();

        if (string.IsNullOrEmpty(line))
        {
            sb.Append('\n');
            return;
        }

        if (line.StartsWith("//", StringComparison.Ordinal))
        {
            AppendIndentedLine(sb, line, indent, indentLevel);
            return;
        }

        // A line that opens a block comment ("/*") which is NOT also closed on the same line
        // ("*/" doesn't appear anywhere after the opener) starts a multi-line comment span.
        // We print the opening line at the current indent, then flip inBlockComment so every
        // following line is swallowed verbatim by HandleActiveBlockComment until the closing
        // line is found.
        //
        // A line like "/* foo */ bar()" (comment closed, then real code after it) deliberately
        // does NOT hit this branch, because it DOES contain "*/" — it falls through to the
        // normal path below, where CountNetBraces (updated below) skips over the "/* foo */"
        // span and correctly counts braces only in "bar()".
        if (
            line.StartsWith("/*", StringComparison.Ordinal)
            && !line.Contains("*/", StringComparison.Ordinal)
        )
        {
            AppendIndentedLine(sb, line, indent, indentLevel);
            inBlockComment = true;
            return;
        }

        // NEW: mirror of the "/*" check above, for a Razor comment opener ("@*") that isn't
        // closed on the same line. See the remarks on inRazorComment for why this text can
        // appear here at all (embedded Razor comments inside a <script> block).
        if (
            line.StartsWith("@*", StringComparison.Ordinal)
            && !line.Contains("*@", StringComparison.Ordinal)
        )
        {
            AppendIndentedLine(sb, line, indent, indentLevel);
            inRazorComment = true;
            return;
        }

        // FIXED: previously this only ever subtracted 1 for a line starting with a single
        // closer, regardless of how many closing brackets actually led the line (e.g. "});"
        // has two). That mismatched the unclamped, whole-line net computed below, and the
        // gap between the two never got corrected — producing permanent, compounding drift
        // on every "foo({ ... });" style call (extremely common in jQuery code), which is
        // exactly the runaway staircase indentation this was producing on real files.
        var leadingClosers = CountLeadingClosers(line);
        if (leadingClosers > 0)
        {
            indentLevel = Math.Max(0, indentLevel - leadingClosers);
        }

        AppendIndentedLine(sb, line, indent, indentLevel);

        // Net bracket delta for the WHOLE line, unclamped (can be negative) — this is the
        // line's true total effect on nesting depth. We already applied the "leadingClosers"
        // portion of that effect above (for correct printing of this line itself), so only
        // the remainder needs to be applied going into the next line.
        var netBraces = CountNetBraces(line);
        var remaining = netBraces + leadingClosers;
        indentLevel = Math.Max(0, indentLevel + remaining);

        // Backtick counting must ignore any backtick that sits inside a same-line "/* ... */"
        // or "@* ... *@" span (e.g. "/* uses `foo` syntax */"), otherwise a comment merely
        // mentioning a template literal could wrongly flip inTemplateLiteral. CountUnescaped
        // is applied to the comment-stripped version of the line for exactly this reason.
        if (CountUnescaped(StripCommentSpans(line), '`') % 2 != 0)
        {
            inTemplateLiteral = true;
        }
    }

    /// <summary>
    /// Counts how many closing bracket characters ('}', ']', ')') appear consecutively at
    /// the very start of the (already-trimmed) line, e.g. "});" → 2, "}" → 1, "foo()" → 0.
    /// </summary>
    private static int CountLeadingClosers(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] is '}' or ']' or ')')
        {
            count++;
        }
        return count;
    }

    private static void HandleActiveTemplateLiteral(
        string rawLine,
        StringBuilder sb,
        ref bool inTemplateLiteral
    )
    {
        sb.Append(rawLine).Append('\n');
        for (int i = 0; i < rawLine.Length; i++)
        {
            if (rawLine[i] == '`' && (i == 0 || rawLine[i - 1] != '\\'))
            {
                inTemplateLiteral = false;
                break;
            }
        }
    }

    /// <summary>
    /// Handles a physical line while we're inside a multi-line "/* ... */" block comment. The
    /// line is appended completely unchanged (no re-indentation — block comments often rely on
    /// their own internal alignment, e.g. ASCII banners or aligned "@param" lines, which must
    /// not be disturbed). If this line contains the closing "*/", the comment span ends and
    /// normal line processing resumes on the next line.
    /// </summary>
    private static void HandleActiveBlockComment(
        string rawLine,
        StringBuilder sb,
        ref bool inBlockComment
    )
    {
        sb.Append(rawLine).Append('\n');

        if (rawLine.Contains("*/", StringComparison.Ordinal))
        {
            inBlockComment = false;
        }
    }

    /// <summary>
    /// NEW: handles a physical line while inside a multi-line Razor comment ("@* ... *@").
    /// Unlike <see cref="HandleActiveBlockComment"/>, this cannot simply dump the whole raw
    /// line, because a Razor comment routinely closes mid-line with live JavaScript trailing
    /// it on the very same physical line (e.g. "... *@function foo() {"). So:
    /// <list type="bullet">
    /// <item><description>If "*@" is not found on this line, the whole line is still inside
    /// the comment — append it verbatim and stay in the comment, exactly like a block
    /// comment.</description></item>
    /// <item><description>If "*@" IS found, everything up to and including it is comment
    /// content and is appended verbatim; inRazorComment is cleared; and whatever text follows
    /// "*@" is treated as a brand-new ordinary line and re-fed through <see cref="ProcessLine"/>
    /// so it gets normal indentation and brace counting, rather than being silently
    /// dropped or left unindented.</description></item>
    /// </list>
    /// </summary>
    private static void HandleActiveRazorComment(
        string rawLine,
        StringBuilder sb,
        string indent,
        ref int indentLevel,
        ref bool inTemplateLiteral,
        ref bool inBlockComment,
        ref bool inRazorComment
    )
    {
        var closeIndex = rawLine.IndexOf("*@", StringComparison.Ordinal);

        if (closeIndex < 0)
        {
            // Comment continues past this line too — nothing to split off yet.
            sb.Append(rawLine).Append('\n');
            return;
        }

        // Comment closes on this line. Print only the comment portion (up to and including
        // "*@") as its own line, exactly as it appeared in the source.
        var commentPortion = rawLine[..(closeIndex + 2)];
        sb.Append(commentPortion).Append('\n');
        inRazorComment = false;

        // Whatever trails "*@" (e.g. "function includeHTML() {var z, i, elmnt, file, xhttp;")
        // is live code, not comment content. Re-run it through the normal per-line pipeline so
        // it gets its own correct indentation and brace-count contribution, instead of being
        // silently swallowed the way it was before this fix.
        var remainder = rawLine[(closeIndex + 2)..];
        if (remainder.Length > 0)
        {
            ProcessLine(
                remainder,
                sb,
                indent,
                ref indentLevel,
                ref inTemplateLiteral,
                ref inBlockComment,
                ref inRazorComment
            );
        }
    }

    private static void AppendIndentedLine(
        StringBuilder sb,
        string line,
        string indent,
        int indentLevel
    )
    {
        sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
        sb.Append(line);
        sb.Append('\n');
    }

    /// <summary>
    /// Net bracket delta for the whole line (opens minus closes), skipping string/comment
    /// content. Unlike before, this is NOT clamped to zero — a line that closes more than it
    /// opens returns a genuinely negative value, which is required for indentLevel to ever
    /// correctly return to a shallower depth.
    /// </summary>
    private static int CountNetBraces(string line)
    {
        var net = 0;
        var inString = false;
        var stringChar = '\0';

        for (int i = 0; i < line.Length; i++)
        {
            if (!inString && i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/')
            {
                break; // Ignore rest of line due to line comment
            }

            // Skip over a "/* ... */" span that both opens and closes on this same line, so
            // any brace characters inside comment prose are never counted as real code. If
            // "*/" isn't found, the comment doesn't close on this line — that multi-line case
            // is handled by inBlockComment/HandleActiveBlockComment in ProcessLine before
            // CountNetBraces is ever called on the following lines, so we just stop scanning
            // this line at the unterminated "/*".
            if (!inString && i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
            {
                var closeIndex = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (closeIndex < 0)
                {
                    break;
                }

                i = closeIndex + 1; // loop's own i++ lands just past "*/"
                continue;
            }

            // NEW: same idea as "/* ... */" above, but for a same-line Razor comment
            // ("@* ... *@"). Handles the (rarer) case where an entire Razor comment — braces
            // and all — sits on one line, e.g. "@* legacy: uses {a} syntax *@".
            if (!inString && i + 1 < line.Length && line[i] == '@' && line[i + 1] == '*')
            {
                var closeIndex = line.IndexOf("*@", i + 2, StringComparison.Ordinal);
                if (closeIndex < 0)
                {
                    break;
                }

                i = closeIndex + 1; // loop's own i++ lands just past "*@"
                continue;
            }

            if (TryHandleStringChar(line, i, ref inString, ref stringChar))
            {
                continue;
            }

            if (!inString)
            {
                net += GetBraceValue(line[i]);
            }
        }

        return net;
    }

    /// <summary>
    /// Returns a copy of <paramref name="line"/> with any same-line "/* ... */" or
    /// "@* ... *@" span(s) removed. Used only before scanning for stray backticks, so a
    /// backtick mentioned inside comment prose (e.g. "/* wrap in `backticks` */") can never be
    /// mistaken for the start/end of a real template literal. Mirrors the same
    /// "unterminated comment ends the scan" behavior as CountNetBraces, for consistency.
    /// </summary>
    private static string StripCommentSpans(string line)
    {
        var sb = new StringBuilder(line.Length);
        var inString = false;
        var stringChar = '\0';
        var i = 0;

        while (i < line.Length)
        {
            if (!inString && i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/')
            {
                break; // rest of line is a line comment; nothing further matters here
            }

            if (!inString && i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
            {
                var closeIndex = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (closeIndex < 0)
                {
                    break;
                }

                i = closeIndex + 2; // skip past "*/", append nothing for the comment itself
                continue;
            }

            if (!inString && i + 1 < line.Length && line[i] == '@' && line[i + 1] == '*')
            {
                var closeIndex = line.IndexOf("*@", i + 2, StringComparison.Ordinal);
                if (closeIndex < 0)
                {
                    break;
                }

                i = closeIndex + 2; // skip past "*@"
                continue;
            }

            if (TryHandleStringChar(line, i, ref inString, ref stringChar))
            {
                sb.Append(line[i]);
                i++;
                continue;
            }

            sb.Append(line[i]);
            i++;
        }

        return sb.ToString();
    }

    private static bool TryHandleStringChar(
        string line,
        int index,
        ref bool inString,
        ref char stringChar
    )
    {
        var c = line[index];
        if (c == '"' || c == '\'' || c == '`')
        {
            if (!inString)
            {
                inString = true;
                stringChar = c;
                return true;
            }

            if (c == stringChar && (index == 0 || line[index - 1] != '\\'))
            {
                inString = false;
                return true;
            }
        }
        return false;
    }

    private static int GetBraceValue(char c)
    {
        if (c == '{' || c == '[' || c == '(')
            return 1;
        if (c == '}' || c == ']' || c == ')')
            return -1;
        return 0;
    }

    private static int CountUnescaped(string line, char target)
    {
        var count = 0;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == target && (i == 0 || line[i - 1] != '\\'))
            {
                count++;
            }
        }
        return count;
    }
}
