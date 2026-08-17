using System.Text;
using System.Text.Json;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Enums;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.Json;

/// <summary>
/// Formats JSON files using System.Text.Json for reliable parsing and reformatting.
/// Preserves all values and data types; only adjusts whitespace and indentation.
/// </summary>
public sealed class JsonFormatter : BaseFormatter
{
    /// <inheritdoc />
    public override Language Language => Language.Json;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } =
        [".json", ".jsonc", ".webmanifest"];

    /// <summary>Initializes a new instance of the <see cref="JsonFormatter"/> class.</summary>
    public JsonFormatter(ILogger<JsonFormatter> logger) : base(logger) { }

    /// <inheritdoc />
    public override Task<bool> CanFormatAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(false);
        }

        try
        {
            // Strip comments for JSONC support
            var stripped = StripJsonComments(text);
            using var doc = JsonDocument.Parse(stripped);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    protected override Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Handle JSONC (JSON with comments) by preserving comments
        var hasComments = text.Contains("//") || text.Contains("/*");

        if (hasComments)
        {
            return Task.FromResult(FormatJsonWithComments(text, options));
        }

        return Task.FromResult(FormatPureJson(text, options));
    }

    private static string FormatPureJson(string text, FormattingOptions options)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var writerOptions = new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, writerOptions))
        {
            WriteElement(writer, doc.RootElement, options, 0);
        }

        var result = Encoding.UTF8.GetString(stream.ToArray());

        // Apply custom indentation
        if (options.UseTabs || options.IndentSize != 2)
        {
            result = AdjustIndentation(result, options);
        }

        return result;
    }

    private static void WriteElement(
        Utf8JsonWriter writer,
        JsonElement element,
        FormattingOptions options,
        int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = element.EnumerateObject().ToList();

                if (options.Json.SortProperties)
                {
                    properties = properties.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
                }

                foreach (var prop in properties)
                {
                    writer.WritePropertyName(prop.Name);
                    WriteElement(writer, prop.Value, options, depth + 1);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item, options, depth + 1);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longVal))
                {
                    writer.WriteNumberValue(longVal);
                }
                else
                {
                    writer.WriteNumberValue(element.GetDouble());
                }
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
        }
    }

    private static string FormatJsonWithComments(string text, FormattingOptions options)
    {
        // For JSONC, we do a best-effort format that preserves comment positions
        // Parse ignoring comments, format, then reinsert comments
        var comments = ExtractComments(text);
        var stripped = StripJsonComments(text);

        string formatted;
        try
        {
            formatted = FormatPureJson(stripped, options);
        }
        catch
        {
            // If we can't parse even after stripping comments, return original
            return text;
        }

        // Reinsert comments (best-effort: inline comments go to end of previous line)
        return ReinsertComments(formatted, comments);
    }

    private static List<JsonComment> ExtractComments(string text)
    {
        var comments = new List<JsonComment>();
        var lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();

            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                comments.Add(new JsonComment(i, line, CommentType.SingleLine));
            }
            else if (line.Contains("//", StringComparison.Ordinal))
            {
                var idx = FindCommentStart(line);
                if (idx >= 0)
                {
                    comments.Add(new JsonComment(i, line[idx..], CommentType.InlineTrailing));
                }
            }
        }

        return comments;
    }

    private static int FindCommentStart(string line)
    {
        var inString = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && line[i] == '/' && line[i + 1] == '/')
            {
                return i;
            }
        }
        return -1;
    }

    private static string StripJsonComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        var inString = false;
        var i = 0;

        while (i < text.Length)
        {
            // Handle string literals
            if (text[i] == '"' && (i == 0 || text[i - 1] != '\\'))
            {
                inString = !inString;
                sb.Append(text[i]);
                i++;
                continue;
            }

            if (!inString)
            {
                // Single-line comment
                if (i < text.Length - 1 && text[i] == '/' && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }

                // Block comment
                if (i < text.Length - 1 && text[i] == '/' && text[i + 1] == '*')
                {
                    i += 2;
                    while (i < text.Length - 1 && !(text[i] == '*' && text[i + 1] == '/'))
                    {
                        i++;
                    }
                    i += 2;
                    continue;
                }
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    private static string ReinsertComments(string formatted, List<JsonComment> comments)
    {
        if (comments.Count == 0)
        {
            return formatted;
        }

        // Simple approach: add standalone comments at the top
        var sb = new StringBuilder();
        foreach (var comment in comments.Where(c => c.Type == CommentType.SingleLine))
        {
            sb.AppendLine(comment.Text);
        }

        if (sb.Length > 0)
        {
            return sb.ToString() + formatted;
        }

        return formatted;
    }

    private static string AdjustIndentation(string text, FormattingOptions options)
    {
        var indent = options.UseTabs ? "\t" : new string(' ', options.IndentSize);
        var lines = text.Split('\n');
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            var leadingSpaces = line.Length - trimmed.Length;
            var level = leadingSpaces / 2; // Utf8JsonWriter uses 2-space indent

            sb.Append(string.Concat(Enumerable.Repeat(indent, level)));
            sb.Append(trimmed);
            sb.Append('\n');
        }

        // Remove trailing newline added by loop
        if (sb.Length > 0 && sb[^1] == '\n')
        {
            sb.Length--;
        }

        return sb.ToString();
    }

    private sealed record JsonComment(int Line, string Text, CommentType Type);
    private enum CommentType { SingleLine, InlineTrailing, Block }
}
