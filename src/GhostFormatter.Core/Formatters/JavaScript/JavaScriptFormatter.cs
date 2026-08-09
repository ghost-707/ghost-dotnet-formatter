using System.Text;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Model;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.JavaScript;

/// <summary>
/// Formats JavaScript and JSX files with proper indentation and spacing.
/// Preserves ASI (Automatic Semicolon Insertion) behavior and template literal contents.
/// </summary>
public sealed class JavaScriptFormatter : BaseFormatter
{
    /// <inheritdoc />
    public override Language Language => Language.JavaScript;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".js", ".mjs", ".jsx"];

    /// <summary>Initializes a new instance of the <see cref="JavaScriptFormatter"/> class.</summary>
    public JavaScriptFormatter(ILogger<JavaScriptFormatter> logger) : base(logger) { }

    /// <inheritdoc />
    protected override Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sb = new StringBuilder();
        var indent = options.UseTabs ? "\t" : new string(' ', options.IndentSize);
        var indentLevel = 0;
        var inTemplateLiteral = false;

        var lines = text.Split('\n');

        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessLine(rawLine, sb, indent, ref indentLevel, ref inTemplateLiteral);
        }

        var result = sb.ToString().TrimEnd('\n');
        return Task.FromResult(result);
    }

    private static void ProcessLine(string rawLine, StringBuilder sb, string indent, ref int indentLevel, ref bool inTemplateLiteral)
    {
        if (inTemplateLiteral)
        {
            HandleActiveTemplateLiteral(rawLine, sb, ref inTemplateLiteral);
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

        if (CountUnescaped(line, '`') % 2 != 0)
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

    private static void HandleActiveTemplateLiteral(string rawLine, StringBuilder sb, ref bool inTemplateLiteral)
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

    private static void AppendIndentedLine(StringBuilder sb, string line, string indent, int indentLevel)
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
                break; // Ignore rest of line due to comment
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

    private static bool TryHandleStringChar(string line, int index, ref bool inString, ref char stringChar)
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
