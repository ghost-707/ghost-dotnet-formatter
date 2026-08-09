using System.Text;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Model;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.Sql;

/// <summary>
/// Formats SQL queries with keyword capitalization, indentation, and alignment.
/// Preserves query semantics — never reorders predicates or expressions.
/// </summary>
public sealed partial class SqlFormatter : BaseFormatter
{
    private static readonly HashSet<string> MajorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "JOIN", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN",
        "FULL JOIN", "CROSS JOIN", "LEFT OUTER JOIN", "RIGHT OUTER JOIN",
        "FULL OUTER JOIN", "ON", "AND", "OR", "ORDER BY", "GROUP BY",
        "HAVING", "UNION", "UNION ALL", "EXCEPT", "INTERSECT",
        "INSERT INTO", "VALUES", "UPDATE", "SET", "DELETE FROM", "DELETE",
        "MERGE", "USING", "WHEN MATCHED", "WHEN NOT MATCHED",
        "CREATE", "ALTER", "DROP", "TRUNCATE",
        "BEGIN", "END", "IF", "ELSE", "WHILE", "RETURN",
        "DECLARE", "EXEC", "EXECUTE", "WITH", "AS",
        "CASE", "WHEN", "THEN", "ELSE", "END",
        "EXISTS", "IN", "BETWEEN", "LIKE", "NOT",
        "IS NULL", "IS NOT NULL", "ASC", "DESC",
        "TOP", "DISTINCT", "INTO", "LIMIT", "OFFSET", "FETCH"
    };

    private static readonly HashSet<string> NewlineKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "JOIN", "INNER JOIN", "LEFT JOIN",
        "RIGHT JOIN", "FULL JOIN", "CROSS JOIN", "LEFT OUTER JOIN",
        "RIGHT OUTER JOIN", "FULL OUTER JOIN", "ON", "ORDER BY",
        "GROUP BY", "HAVING", "UNION", "UNION ALL", "INSERT INTO",
        "VALUES", "UPDATE", "SET", "DELETE", "DELETE FROM", "MERGE",
        "USING", "WHEN MATCHED", "WHEN NOT MATCHED", "WITH"
    };

    /// <inheritdoc />
    public override Language Language => Language.Sql;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".sql"];

    /// <summary>Initializes a new instance of the <see cref="SqlFormatter"/> class.</summary>
    public SqlFormatter(ILogger<SqlFormatter> logger) : base(logger) { }

    /// <inheritdoc />
    protected override Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken)
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
            if (char.IsWhiteSpace(sql[i]))
            {
                i++;
                continue;
            }

            if (TryParseComment(sql, ref i, tokens) ||
                TryParseString(sql, ref i, tokens) ||
                TryParseBracketIdentifier(sql, ref i, tokens) ||
                TryParsePunctuation(sql, ref i, tokens) ||
                TryParseWord(sql, ref i, tokens))
            {
                continue;
            }

            tokens.Add(new SqlToken(SqlTokenType.Other, sql[i].ToString()));
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
        while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '@' || sql[i] == '#'))
        {
            i++;
        }

        if (i > wordStart)
        {
            var word = sql[wordStart..i];
            var type = MajorKeywords.Contains(word) ? SqlTokenType.Keyword : SqlTokenType.Identifier;
            tokens.Add(new SqlToken(type, word));
            return true;
        }

        return false;
    }

    private static string FormatTokens(List<SqlToken> tokens, FormattingOptions options)
    {
        var sb = new StringBuilder();
        var indent = options.UseTabs ? "\t" : new string(' ', options.IndentSize);
        var indentLevel = 0;
        var i = 0; // Using a while loop resolves S127 (modifying loop variable)

        while (i < tokens.Count)
        {
            var token = tokens[i];

            switch (token.Type)
            {
                case SqlTokenType.Keyword:
                    HandleKeyword(tokens, ref i, sb, options, indent, indentLevel);
                    break;
                case SqlTokenType.Punctuation:
                    HandlePunctuation(token, sb, options, indent, ref indentLevel);
                    break;
                case SqlTokenType.Comment:
                    HandleComment(token, sb, indent, indentLevel);
                    break;
                default:
                    HandleDefault(token, sb);
                    break;
            }
            i++;
        }

        return sb.ToString().TrimEnd();
    }

    private static void HandleKeyword(List<SqlToken> tokens, ref int index, StringBuilder sb, FormattingOptions options, string indent, int indentLevel)
    {
        var token = tokens[index];
        var word = token.Value;

        var multiWord = TryGetMultiWordKeyword(tokens, index);
        if (multiWord is not null)
        {
            word = multiWord;
            index += multiWord.Split(' ').Length - 1;
        }

        var upper = options.Sql.UppercaseKeywords ? word.ToUpperInvariant() : word;

        if (NewlineKeywords.Contains(upper))
        {
            if (sb.Length > 0)
                sb.Append('\n');

            var spaces = IsSubClause(upper) ? indentLevel + 1 : indentLevel;
            sb.Append(string.Concat(Enumerable.Repeat(indent, spaces)));
        }
        else if (sb.Length > 0 && sb[^1] != '\n' && sb[^1] != ' ' && sb[^1] != '(')
        {
            sb.Append(' ');
        }

        sb.Append(upper);
    }

    private static void HandlePunctuation(SqlToken token, StringBuilder sb, FormattingOptions options, string indent, ref int indentLevel)
    {
        if (token.Value == "(")
        {
            sb.Append(" (");
            indentLevel++;
        }
        else if (token.Value == ")")
        {
            indentLevel = Math.Max(0, indentLevel - 1);
            sb.Append(')');
        }
        else if (token.Value == ",")
        {
            if (options.Sql.OneColumnPerLine)
            {
                sb.Append(",\n");
                sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel + 1)));
            }
            else
            {
                sb.Append(", ");
            }
        }
        else if (token.Value == ";")
        {
            sb.Append(";\n");
        }
        else
        {
            sb.Append(token.Value);
        }
    }

    private static void HandleComment(SqlToken token, StringBuilder sb, string indent, int indentLevel)
    {
        if (sb.Length > 0 && sb[^1] != '\n')
        {
            sb.Append(' ');
        }
        sb.Append(token.Value).Append('\n');
        sb.Append(string.Concat(Enumerable.Repeat(indent, indentLevel)));
    }

    private static void HandleDefault(SqlToken token, StringBuilder sb)
    {
        if (sb.Length > 0 && sb[^1] != '\n' && sb[^1] != ' ' && sb[^1] != '(')
        {
            sb.Append(' ');
        }
        sb.Append(token.Value);
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
        return keyword.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
               keyword.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
               keyword.Equals("ON", StringComparison.OrdinalIgnoreCase);
    }
}

internal enum SqlTokenType { Keyword, Identifier, String, Punctuation, Comment, Operator, Other }

internal sealed record SqlToken(SqlTokenType Type, string Value);
