using System.Text;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Enums;
using GhostFormatter.Abstractions.Model;
using GhostFormatter.Core.Modals;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.Sql;

/// <summary>
/// Formats SQL queries with keyword capitalization, indentation, and alignment.
/// Preserves query semantics — never reorders predicates or expressions.
/// </summary>
/// <remarks>
/// Indentation is driven by two combined counters, mirroring the approach used by
/// <c>RazorFormatter</c>:
/// <list type="bullet">
/// <item><description><c>indentLevel</c> — parenthesis nesting from "(" / ")" (as
/// before).</description></item>
/// <item><description><c>blockStack</c> — a stack of "BEGIN" / "CASE" markers for
/// procedural block and CASE-expression nesting. A single shared stack is used for both
/// because "END" always closes whichever of the two is innermost, so a stack is what lets
/// nested constructs like "IF ... BEGIN CASE ... END END" dedent correctly at each level
/// instead of drifting. "BEGIN TRAN"/"BEGIN TRANSACTION" is deliberately excluded from this
/// — it's closed by COMMIT/ROLLBACK, never "END", so pushing it would drift every line for
/// the rest of the batch.</description></item>
/// </list>
/// Multi-word keyword phrases ("INSERT INTO", "LEFT JOIN", "GROUP BY", "ORDER BY", etc.) are
/// recognized during tokenizing itself via lookahead (<see cref="PeekNextWord"/>), not only
/// after a token is already classified as a keyword — otherwise a phrase whose first word
/// isn't independently a keyword (e.g. "INSERT" and "GROUP" alone aren't, but "INSERT INTO"
/// and "GROUP BY" are) would tokenize as a plain identifier and never reach any indentation
/// logic at all.
/// A handful of T-SQL keywords are contextually overloaded (the same spelling means two
/// different things depending on what surrounds it) and are disambiguated structurally — see
/// the "ON", "IF", and "BEGIN" handling in <see cref="HandleKeyword"/>.
/// After all tokens are formatted, a final pass (<see cref="WrapLongLines"/>) breaks any
/// remaining line that still exceeds <see cref="FormattingOptions.MaxLineLength"/>, so the
/// output never requires horizontal scrolling even for long column lists, IN-lists, or
/// unwrapped predicates.
/// </remarks>
public sealed partial class SqlFormatter : BaseFormatter
{
    private static readonly HashSet<string> MajorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT",
        "FROM",
        "WHERE",
        "JOIN",
        "INNER JOIN",
        "LEFT JOIN",
        "RIGHT JOIN",
        "FULL JOIN",
        "CROSS JOIN",
        "LEFT OUTER JOIN",
        "RIGHT OUTER JOIN",
        "FULL OUTER JOIN",
        "ON",
        "AND",
        "OR",
        "ORDER BY",
        "GROUP BY",
        "HAVING",
        "UNION",
        "UNION ALL",
        "EXCEPT",
        "INTERSECT",
        "INSERT INTO",
        "VALUES",
        "UPDATE",
        "SET",
        "DELETE FROM",
        "DELETE",
        "MERGE",
        "USING",
        "WHEN MATCHED",
        "WHEN NOT MATCHED",
        "CREATE",
        "ALTER",
        "DROP",
        "TRUNCATE",
        "CREATE OR ALTER",
        "BEGIN",
        "END",
        "IF",
        "ELSE",
        "WHILE",
        "RETURN",
        "DECLARE",
        "EXEC",
        "EXECUTE",
        "WITH",
        "AS",
        "CASE",
        "WHEN",
        "THEN",
        "EXISTS",
        "IN",
        "BETWEEN",
        "LIKE",
        "NOT",
        "IS",
        "IS NULL",
        "IS NOT NULL",
        "ASC",
        "DESC",
        "TOP",
        "DISTINCT",
        "INTO",
        "LIMIT",
        "OFFSET",
        "FETCH",
        "GO",
    };

    private static readonly HashSet<string> NewlineKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT",
        "FROM",
        "WHERE",
        "JOIN",
        "INNER JOIN",
        "LEFT JOIN",
        "RIGHT JOIN",
        "FULL JOIN",
        "CROSS JOIN",
        "LEFT OUTER JOIN",
        "RIGHT OUTER JOIN",
        "FULL OUTER JOIN",
        "ON",
        "ORDER BY",
        "GROUP BY",
        "HAVING",
        "UNION",
        "UNION ALL",
        "INSERT INTO",
        "VALUES",
        "UPDATE",
        "SET",
        "DELETE",
        "DELETE FROM",
        "MERGE",
        "USING",
        "WHEN MATCHED",
        "WHEN NOT MATCHED",
        "WITH",
        "AND",
        "OR",
    };

    /// <inheritdoc />
    public override Language Language => Language.Sql;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".sql"];

    /// <summary>Initializes a new instance of the <see cref="SqlFormatter"/> class.</summary>
    public SqlFormatter(ILogger<SqlFormatter> logger)
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
        var result = FormatTokens(tokens, options);

        return Task.FromResult(result);
    }

    private static List<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        var i = 0;

        while (i < sql.Length)
        {
            var sawNewline = false;
            while (i < sql.Length && char.IsWhiteSpace(sql[i]))
            {
                if (sql[i] == '\n')
                {
                    sawNewline = true;
                }
                i++;
            }

            if (i >= sql.Length)
            {
                break;
            }

            var countBefore = tokens.Count;

            if (
                TryParseComment(sql, ref i, tokens)
                || TryParseString(sql, ref i, tokens)
                || TryParseBracketIdentifier(sql, ref i, tokens)
                || TryParsePunctuation(sql, ref i, tokens)
                || TryParseWord(sql, ref i, tokens)
            )
            {
                if (sawNewline && tokens.Count > countBefore)
                {
                    tokens[^1] = tokens[^1] with { PrecededByNewline = true };
                }
                continue;
            }

            tokens.Add(new SqlToken(SqlTokenType.Other, sql[i].ToString(), sawNewline));
            i++;
        }

        return tokens;
    }

    private static bool TryParseComment(string sql, ref int i, List<SqlToken> tokens)
    {
        if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
        {
            var end = sql.IndexOf('\n', i);
            if (end < 0)
                end = sql.Length;
            tokens.Add(new SqlToken(SqlTokenType.Comment, sql[i..end]));
            i = end;
            return true;
        }

        if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
        {
            var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
            if (end < 0)
                end = sql.Length - 2;
            end += 2;
            tokens.Add(new SqlToken(SqlTokenType.Comment, sql[i..end]));
            i = end;
            return true;
        }

        return false;
    }

    private static bool TryParseString(string sql, ref int i, List<SqlToken> tokens)
    {
        if (sql[i] != '\'')
            return false;

        var end = i + 1;
        while (end < sql.Length)
        {
            if (sql[end] == '\'' && (end + 1 >= sql.Length || sql[end + 1] != '\''))
            {
                end++;
                break;
            }
            if (sql[end] == '\'' && end + 1 < sql.Length && sql[end + 1] == '\'')
            {
                end += 2;
                continue;
            }
            end++;
        }
        tokens.Add(new SqlToken(SqlTokenType.String, sql[i..end]));
        i = end;
        return true;
    }

    private static bool TryParseBracketIdentifier(string sql, ref int i, List<SqlToken> tokens)
    {
        if (sql[i] != '[')
            return false;

        var end = sql.IndexOf(']', i + 1) + 1;
        if (end == 0)
            end = sql.Length;
        tokens.Add(new SqlToken(SqlTokenType.Identifier, sql[i..end]));
        i = end;
        return true;
    }

    private static bool TryParsePunctuation(string sql, ref int i, List<SqlToken> tokens)
    {
        if (!"(),;.*+=<>!".Contains(sql[i]))
            return false;

        if (i + 1 < sql.Length)
        {
            var twoChar = sql.Substring(i, 2);
            if (twoChar is "<>" or "<=" or ">=" or "!=" or "+=")
            {
                tokens.Add(new SqlToken(SqlTokenType.Operator, twoChar));
                i += 2;
                return true;
            }
        }
        tokens.Add(new SqlToken(SqlTokenType.Punctuation, sql[i].ToString()));
        i++;
        return true;
    }

    private static bool TryParseWord(string sql, ref int i, List<SqlToken> tokens)
    {
        var wordStart = i;
        while (
            i < sql.Length
            && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '@' || sql[i] == '#')
        )
        {
            i++;
        }

        if (i <= wordStart)
        {
            return false;
        }

        var word = sql[wordStart..i];
        var type = SqlTokenType.Identifier;

        if (MajorKeywords.Contains(word))
        {
            type = SqlTokenType.Keyword;
        }
        else
        {
            var (next1, afterNext1) = PeekNextWord(sql, i);
            if (next1 is not null)
            {
                if (MajorKeywords.Contains($"{word} {next1}"))
                {
                    type = SqlTokenType.Keyword;
                }
                else
                {
                    var (next2, _) = PeekNextWord(sql, afterNext1);
                    if (next2 is not null && MajorKeywords.Contains($"{word} {next1} {next2}"))
                    {
                        type = SqlTokenType.Keyword;
                    }
                }
            }
        }

        tokens.Add(new SqlToken(type, word));
        return true;
    }

    /// <summary>
    /// Looks ahead from <paramref name="from"/> for the next identifier-shaped word, skipping
    /// leading whitespace, WITHOUT consuming anything — used only to decide token
    /// classification in <see cref="TryParseWord"/>. Returns (null, from) if no word starts
    /// there (e.g. punctuation or end of input comes next).
    /// </summary>
    private static (string? Word, int NextIndex) PeekNextWord(string sql, int from)
    {
        var i = from;
        while (i < sql.Length && char.IsWhiteSpace(sql[i]))
        {
            i++;
        }

        var start = i;
        while (
            i < sql.Length
            && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '@' || sql[i] == '#')
        )
        {
            i++;
        }

        return i > start ? (sql[start..i], i) : (null, from);
    }

    private static string FormatTokens(List<SqlToken> tokens, FormattingOptions options)
    {
        var sb = new StringBuilder();
        var indent = options.UseTabs ? "\t" : new string(' ', options.IndentSize);
        var indentLevel = 0;
        var blockStack = new List<string>();

        var index = 0;

        while (index < tokens.Count)
        {
            var token = tokens[index];

            switch (token.Type)
            {
                case SqlTokenType.Keyword:
                    HandleKeyword(tokens, ref index, sb, options, indent, indentLevel, blockStack);
                    break;
                case SqlTokenType.Punctuation:
                    HandlePunctuation(
                        tokens,
                        index,
                        sb,
                        options,
                        indent,
                        ref indentLevel,
                        blockStack
                    );
                    break;
                case SqlTokenType.Comment:
                    HandleComment(token, sb, indent, indentLevel, blockStack);
                    break;
                case SqlTokenType.Operator:
                    AppendOperator(sb, token.Value, indent, indentLevel + blockStack.Count);
                    break;
                default:
                    HandleDefault(token, sb, indent, indentLevel, blockStack);
                    break;
            }
            index++;
        }

        var formatted = sb.ToString().TrimEnd();
        return WrapLongLines(formatted, options, indent);
    }

    private static void HandleKeyword(
        List<SqlToken> tokens,
        ref int index,
        StringBuilder sb,
        FormattingOptions options,
        string indent,
        int indentLevel,
        List<string> blockStack
    )
    {
        var token = tokens[index];
        var word = token.Value;

        var multiWord = TryGetMultiWordKeyword(tokens, index);
        if (multiWord is not null)
        {
            word = multiWord;
            index += multiWord.Split(' ').Length - 1;
        }

        var structural = word.ToUpperInvariant();
        var upper = options.Sql.UppercaseKeywords ? word.ToUpperInvariant() : word;
        var depth = indentLevel + blockStack.Count;

        if (structural == "GO")
        {
            EmitBatchSeparator(sb, upper);
            blockStack.Clear();
            return;
        }

        if (structural == "VALUES")
        {
            while (sb.Length > 0 && (sb[^1] == ' ' || sb[^1] == '\t'))
            {
                sb.Length--;
            }
            if (sb.Length > 0 && sb[^1] != '\n')
            {
                sb.Append('\n');
            }
            if (sb.Length > 0 && sb[^1] == '\n' && (sb.Length < 2 || sb[^2] != '\n'))
            {
                sb.Append('\n');
            }
            sb.Append(string.Concat(Enumerable.Repeat(indent, Math.Max(0, depth))));
            sb.Append(upper);
            return;
        }

        switch (structural)
        {
            case "BEGIN":
                var isTransactionStart =
                    index + 1 < tokens.Count
                    && (
                        tokens[index + 1].Value.Equals("TRAN", StringComparison.OrdinalIgnoreCase)
                        || tokens[index + 1]
                            .Value.Equals("TRANSACTION", StringComparison.OrdinalIgnoreCase)
                    );
                AppendNewlineKeywordLine(sb, upper, indent, depth);
                if (!isTransactionStart)
                {
                    blockStack.Add("BEGIN");
                }
                return;

            case "CASE":
                AppendInline(sb, upper, indent, depth);
                blockStack.Add("CASE");
                return;

            case "END":
                if (blockStack.Count > 0)
                {
                    blockStack.RemoveAt(blockStack.Count - 1);
                }
                AppendNewlineKeywordLine(sb, upper, indent, indentLevel + blockStack.Count);
                return;

            case "WHEN":
            case "ELSE":
            case "WHILE":
                AppendNewlineKeywordLine(sb, upper, indent, depth);
                return;

            case "IF":
                if (IsDropIfExists(tokens, index))
                {
                    AppendInline(sb, upper, indent, depth);
                    return;
                }
                AppendNewlineKeywordLine(sb, upper, indent, depth);
                return;

            case "ON":
                if (index + 1 < tokens.Count && tokens[index + 1].Value == ";")
                {
                    AppendInline(sb, upper, indent, depth);
                    return;
                }
                break;
        }

        if (NewlineKeywords.Contains(upper))
        {
            var spaces = IsSubClause(upper) ? depth + 1 : depth;
            AppendNewlineKeywordLine(sb, upper, indent, spaces);
            return;
        }

        AppendInline(sb, upper, indent, depth);
    }

    private static bool IsDropIfExists(List<SqlToken> tokens, int ifIndex)
    {
        if (
            ifIndex + 1 >= tokens.Count
            || !tokens[ifIndex + 1].Value.Equals("EXISTS", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        var lookback = Math.Max(0, ifIndex - 4);
        for (var j = ifIndex - 1; j >= lookback; j--)
        {
            if (tokens[j].Value.Equals("DROP", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (tokens[j].Value == ";")
            {
                break;
            }
        }
        return false;
    }

    private static void AppendNewlineKeywordLine(
        StringBuilder sb,
        string text,
        string indent,
        int depth
    )
    {
        if (sb.Length > 0)
        {
            sb.Append('\n');
        }
        sb.Append(string.Concat(Enumerable.Repeat(indent, Math.Max(0, depth))));
        sb.Append(text);
    }

    private static void AppendInline(StringBuilder sb, string text, string indent, int depth)
    {
        if (sb.Length == 0)
        {
            sb.Append(text);
            return;
        }

        if (sb[^1] == '\n')
        {
            sb.Append(string.Concat(Enumerable.Repeat(indent, Math.Max(0, depth))));
            sb.Append(text);
            return;
        }

        if (sb[^1] != ' ' && sb[^1] != '(' && sb[^1] != '.')
        {
            sb.Append(' ');
        }
        sb.Append(text);
    }

    private static void AppendOperator(StringBuilder sb, string op, string indent, int depth)
    {
        AppendInline(sb, op, indent, depth);
        sb.Append(' ');
    }

    private static void EmitBatchSeparator(StringBuilder sb, string goKeyword)
    {
        while (sb.Length > 0 && sb[^1] == '\n')
        {
            sb.Length--;
        }
        sb.Append("\n\n").Append(goKeyword).Append('\n');
    }

    private static void HandlePunctuation(
        List<SqlToken> tokens,
        int index,
        StringBuilder sb,
        FormattingOptions options,
        string indent,
        ref int indentLevel,
        List<string> blockStack
    )
    {
        var token = tokens[index];

        switch (token.Value)
        {
            case "(":
                var isInsertColumnList = IsInsertColumnListParen(tokens, index);
                var noSpaceBeforeParen =
                    index > 0
                    && (
                        tokens[index - 1].Type == SqlTokenType.Identifier
                        || tokens[index - 1].Value is "." or "@" or "#" or "("
                    );

                if (
                    noSpaceBeforeParen
                    || sb.Length == 0
                    || sb[^1] == '\n'
                    || sb[^1] == ' '
                    || sb[^1] == '('
                )
                {
                    sb.Append('(');
                }
                else
                {
                    sb.Append(" (");
                }

                if (isInsertColumnList && options.Sql.OneColumnPerLine)
                {
                    sb.Append('\n');
                    indentLevel++;
                    sb.Append(
                        string.Concat(Enumerable.Repeat(indent, indentLevel + blockStack.Count + 1))
                    );
                }
                else
                {
                    indentLevel++;
                }
                return;

            case ")":
                indentLevel = Math.Max(0, indentLevel - 1);
                sb.Append(')');
                return;

            case ",":
                if (options.Sql.OneColumnPerLine)
                {
                    sb.Append(",\n");
                    sb.Append(
                        string.Concat(Enumerable.Repeat(indent, indentLevel + blockStack.Count + 1))
                    );
                }
                else
                {
                    sb.Append(", ");
                }
                return;

            case ";":
                sb.Append(";\n");
                return;

            case "=":
            case "*":
            case "+":
            case "<":
            case ">":
            case "!":
                AppendOperator(sb, token.Value, indent, indentLevel + blockStack.Count);
                return;

            default:
                sb.Append(token.Value);
                return;
        }
    }

    private static bool IsInsertColumnListParen(List<SqlToken> tokens, int parenIndex)
    {
        if (parenIndex <= 0)
        {
            return false;
        }

        var i = parenIndex - 1;
        while (i >= 0 && (tokens[i].Type == SqlTokenType.Identifier || tokens[i].Value == "."))
        {
            i--;
        }

        if (i >= 0 && tokens[i].Type == SqlTokenType.Keyword)
        {
            var val = tokens[i].Value;
            return val.Equals("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                || val.Equals("INSERT", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static void HandleComment(
        SqlToken token,
        StringBuilder sb,
        string indent,
        int indentLevel,
        List<string> blockStack
    )
    {
        var isMultiLine = token.Value.StartsWith("/*", StringComparison.Ordinal);
        var depth = indentLevel + blockStack.Count;
        var defaultIndent = string.Concat(Enumerable.Repeat(indent, Math.Max(0, depth)));

        if (isMultiLine)
        {
            // Ensure multi-line comments occupy their own line with a blank line before where they start
            while (sb.Length > 0 && (sb[^1] == ' ' || sb[^1] == '\t'))
            {
                sb.Length--;
            }

            if (sb.Length > 0)
            {
                var newlineCount = 0;
                for (var i = sb.Length - 1; i >= 0 && sb[i] == '\n'; i--)
                {
                    newlineCount++;
                }

                if (newlineCount == 0)
                {
                    sb.Append("\n\n");
                }
                else if (newlineCount == 1)
                {
                    sb.Append('\n');
                }
            }

            sb.Append(defaultIndent);
            sb.Append(token.Value).Append('\n');
            sb.Append(defaultIndent);
            return;
        }

        // Single-line comments (-- ...)
        if (IsAtStartOfLine(sb, out var trailingIndent))
        {
            var targetIndent = trailingIndent.Length > 0 ? trailingIndent : defaultIndent;
            if (trailingIndent.Length == 0 && sb.Length > 0 && sb[^1] != '\n')
            {
                sb.Append('\n');
            }
            if (trailingIndent.Length == 0)
            {
                sb.Append(targetIndent);
            }

            sb.Append(token.Value).Append('\n');
            sb.Append(targetIndent);
        }
        else if (token.PrecededByNewline)
        {
            if (sb.Length > 0 && sb[^1] != '\n')
            {
                sb.Append('\n');
            }
            sb.Append(defaultIndent);
            sb.Append(token.Value).Append('\n');
            sb.Append(defaultIndent);
        }
        else
        {
            if (sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '\n' && sb[^1] != '(')
            {
                sb.Append(' ');
            }
            sb.Append(token.Value).Append('\n');
            sb.Append(defaultIndent);
        }
    }

    private static bool IsAtStartOfLine(StringBuilder sb, out string trailingIndent)
    {
        var lastNewline = -1;
        for (var i = sb.Length - 1; i >= 0; i--)
        {
            if (sb[i] == '\n')
            {
                lastNewline = i;
                break;
            }
        }

        var start = lastNewline + 1;
        for (var i = start; i < sb.Length; i++)
        {
            if (sb[i] != ' ' && sb[i] != '\t')
            {
                trailingIndent = string.Empty;
                return false;
            }
        }

        trailingIndent = sb.ToString(start, sb.Length - start);
        return true;
    }

    private static void HandleDefault(
        SqlToken token,
        StringBuilder sb,
        string indent,
        int indentLevel,
        List<string> blockStack
    )
    {
        AppendInline(sb, token.Value, indent, indentLevel + blockStack.Count);
    }

    private static string? TryGetMultiWordKeyword(List<SqlToken> tokens, int index)
    {
        if (index + 1 >= tokens.Count)
            return null;

        var combined = $"{tokens[index].Value} {tokens[index + 1].Value}";

        if (index + 2 < tokens.Count)
        {
            var threeWord = $"{combined} {tokens[index + 2].Value}";
            if (MajorKeywords.Contains(threeWord))
                return threeWord;
        }

        if (MajorKeywords.Contains(combined))
            return combined;

        return null;
    }

    private static bool IsSubClause(string keyword)
    {
        return keyword.Equals("AND", StringComparison.OrdinalIgnoreCase)
            || keyword.Equals("OR", StringComparison.OrdinalIgnoreCase)
            || keyword.Equals("ON", StringComparison.OrdinalIgnoreCase);
    }

    #region Long line wrapping

    private static string WrapLongLines(
        string formatted,
        FormattingOptions options,
        string indentUnit
    )
    {
        var outputLines = new List<string>();
        foreach (var line in formatted.Split('\n'))
        {
            outputLines.AddRange(WrapLine(line, options, indentUnit));
        }
        return string.Join('\n', outputLines);
    }

    private static List<string> WrapLine(string line, FormattingOptions options, string indentUnit)
    {
        if (line.Length <= options.MaxLineLength)
        {
            return [line];
        }

        var leadingLen = 0;
        while (leadingLen < line.Length && (line[leadingLen] == ' ' || line[leadingLen] == '\t'))
        {
            leadingLen++;
        }

        var content = line[leadingLen..];

        if (content.StartsWith("--", StringComparison.Ordinal))
        {
            return [line];
        }

        var indentLevel = indentUnit.Length > 0 ? leadingLen / indentUnit.Length : 0;

        var bracketWrapped = TryWrapAtBracket(content, indentLevel, indentUnit);
        if (bracketWrapped is not null)
        {
            return ExpandAndRewrap(bracketWrapped, options, indentUnit);
        }

        var commaWrapped = TryWrapAtTopLevelCommas(content, indentLevel, indentUnit);
        if (commaWrapped is not null)
        {
            return ExpandAndRewrap(commaWrapped, options, indentUnit);
        }

        return WordWrap(content, indentLevel, indentUnit, options);
    }

    private static List<string> ExpandAndRewrap(
        string multiLine,
        FormattingOptions options,
        string indentUnit
    )
    {
        var result = new List<string>();
        foreach (var l in multiLine.Split('\n'))
        {
            if (l.Length > options.MaxLineLength)
            {
                result.AddRange(WrapLine(l, options, indentUnit));
            }
            else
            {
                result.Add(l);
            }
        }
        return result;
    }

    private static string? TryWrapAtBracket(string content, int indentLevel, string indentUnit)
    {
        var openIndex = FindFirstUnquotedParen(content);
        if (openIndex < 0)
        {
            return null;
        }

        var closeIndex = FindMatchingCloseParen(content, openIndex);
        if (closeIndex < 0 || closeIndex <= openIndex + 1)
        {
            return null;
        }

        var inner = content[(openIndex + 1)..closeIndex];
        if (string.IsNullOrWhiteSpace(inner))
        {
            return null;
        }

        var parts = SplitTopLevel(inner, ',');
        if (parts.Count < 2)
        {
            return null;
        }

        var prefix = content[..(openIndex + 1)];
        var suffix = content[closeIndex..];

        var baseIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel));
        var argIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel + 1));

        var sb = new StringBuilder();
        sb.Append(baseIndent).Append(prefix.TrimEnd());
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i].Trim();
            var trailer = i == parts.Count - 1 ? "" : ",";
            sb.Append('\n').Append(argIndent).Append(part).Append(trailer);
        }
        sb.Append('\n').Append(baseIndent).Append(suffix.TrimStart());
        return sb.ToString();
    }

    private static string? TryWrapAtTopLevelCommas(
        string content,
        int indentLevel,
        string indentUnit
    )
    {
        var parts = SplitTopLevel(content, ',');
        if (parts.Count < 2)
        {
            return null;
        }

        var baseIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel));
        var itemIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel + 1));

        var sb = new StringBuilder();
        sb.Append(baseIndent).Append(parts[0].TrimEnd()).Append(',');
        for (var i = 1; i < parts.Count; i++)
        {
            var part = parts[i].Trim();
            var trailer = i == parts.Count - 1 ? "" : ",";
            sb.Append('\n').Append(itemIndent).Append(part).Append(trailer);
        }
        return sb.ToString();
    }

    private static List<string> WordWrap(
        string content,
        int indentLevel,
        string indentUnit,
        FormattingOptions options
    )
    {
        var words = TokenizeWords(content);
        if (words.Count < 2)
        {
            return [string.Concat(Enumerable.Repeat(indentUnit, indentLevel)) + content];
        }

        var baseIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel));
        var continuationIndent = string.Concat(Enumerable.Repeat(indentUnit, indentLevel + 1));

        var outputLines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            var indentForCandidate = outputLines.Count == 0 ? baseIndent : continuationIndent;
            var candidateLength = indentForCandidate.Length + current.Length + 1 + word.Length;
            if (candidateLength <= options.MaxLineLength)
            {
                current.Append(' ').Append(word);
            }
            else
            {
                outputLines.Add(
                    (outputLines.Count == 0 ? baseIndent : continuationIndent) + current
                );
                current.Clear();
                current.Append(word);
            }
        }

        if (current.Length > 0)
        {
            outputLines.Add((outputLines.Count == 0 ? baseIndent : continuationIndent) + current);
        }

        return outputLines;
    }

    private static List<string> TokenizeWords(string s)
    {
        var words = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
            if (i >= s.Length)
            {
                break;
            }

            var start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]))
            {
                if (s[i] == '\'')
                {
                    i = SkipQuotedSql(s, i);
                    continue;
                }
                i++;
            }
            words.Add(s[start..i]);
        }
        return words;
    }

    private static int FindFirstUnquotedParen(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\'')
            {
                i = SkipQuotedSql(s, i);
                continue;
            }
            if (s[i] == '[')
            {
                i = SkipBracketIdentifier(s, i);
                continue;
            }
            if (s[i] == '(')
            {
                return i;
            }
            i++;
        }
        return -1;
    }

    private static int FindMatchingCloseParen(string s, int openIndex)
    {
        var depth = 0;
        var i = openIndex;
        while (i < s.Length)
        {
            if (s[i] == '\'')
            {
                i = SkipQuotedSql(s, i);
                continue;
            }
            if (s[i] == '[')
            {
                i = SkipBracketIdentifier(s, i);
                continue;
            }
            if (s[i] == '(')
            {
                depth++;
            }
            else if (s[i] == ')')
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

    private static List<string> SplitTopLevel(string s, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        var i = 0;

        while (i < s.Length)
        {
            if (s[i] == '\'')
            {
                i = SkipQuotedSql(s, i);
                continue;
            }
            if (s[i] == '[')
            {
                i = SkipBracketIdentifier(s, i);
                continue;
            }
            if (s[i] == '(')
            {
                depth++;
            }
            else if (s[i] == ')')
            {
                depth--;
            }
            else if (s[i] == separator && depth == 0)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
            i++;
        }
        parts.Add(s[start..]);
        return parts;
    }

    private static int SkipQuotedSql(string s, int quoteIndex)
    {
        var i = quoteIndex + 1;
        while (i < s.Length)
        {
            if (s[i] == '\'' && i + 1 < s.Length && s[i + 1] == '\'')
            {
                i += 2;
                continue;
            }
            if (s[i] == '\'')
            {
                return i + 1;
            }
            i++;
        }
        return s.Length;
    }

    private static int SkipBracketIdentifier(string s, int openIndex)
    {
        var close = s.IndexOf(']', openIndex + 1);
        return close < 0 ? s.Length : close + 1;
    }

    #endregion
}
