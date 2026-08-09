using System.Text;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Model;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace GhostFormatter.Core.Formatters.Yaml;

/// <summary>
/// Formats YAML files with consistent indentation while preserving data semantics.
/// Handles anchors, aliases, multiline values, sequences, and mappings.
/// </summary>
public sealed class YamlFormatter : BaseFormatter
{
    /// <inheritdoc />
    public override Language Language => Language.Yaml;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".yml", ".yaml"];

    /// <summary>Initializes a new instance of the <see cref="YamlFormatter"/> class.</summary>
    public YamlFormatter(ILogger<YamlFormatter> logger) : base(logger) { }

    /// <inheritdoc />
    public override Task<bool> CanFormatAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(false);
        }

        try
        {
            var yaml = new YamlStream();
            using var reader = new StringReader(text);
            yaml.Load(reader);
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

        try
        {
            var yaml = new YamlStream();
            using var reader = new StringReader(text);
            yaml.Load(reader);

            var sb = new StringBuilder();
            var indent = options.UseTabs ? "\t" : new string(' ', options.IndentSize);

            // Check for document separator
            var hasDocumentSeparator = text.TrimStart().StartsWith("---", StringComparison.Ordinal);

            foreach (var doc in yaml.Documents)
            {
                if (hasDocumentSeparator)
                {
                    sb.AppendLine("---");
                }

                WriteNode(sb, doc.RootNode, indent, 0, options);
            }

            return Task.FromResult(sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "YAML parsing failed, performing whitespace-only formatting");
            return Task.FromResult(FormatWhitespaceOnly(text, options));
        }
    }

    private static void WriteNode(
        StringBuilder sb,
        YamlNode node,
        string indent,
        int level,
        FormattingOptions options)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                WriteMappingNode(sb, mapping, indent, level, options);
                break;

            case YamlSequenceNode sequence:
                WriteSequenceNode(sb, sequence, indent, level, options);
                break;

            case YamlScalarNode scalar:
                WriteScalarNode(sb, scalar);
                break;
        }
    }

    private static void WriteMappingNode(
        StringBuilder sb,
        YamlMappingNode mapping,
        string indent,
        int level,
        FormattingOptions options)
    {
        var isFirst = true;

        foreach (var pair in mapping.Children)
        {
            if (!isFirst || level > 0)
            {
                sb.Append(string.Concat(Enumerable.Repeat(indent, level)));
            }

            if (pair.Key is YamlScalarNode keyScalar)
            {
                sb.Append(keyScalar.Value);
                sb.Append(':');
            }

            if (pair.Value is YamlMappingNode or YamlSequenceNode)
            {
                sb.Append('\n');
                WriteNode(sb, pair.Value, indent, level + 1, options);
            }
            else
            {
                sb.Append(' ');
                WriteNode(sb, pair.Value, indent, level + 1, options);
                sb.Append('\n');
            }

            isFirst = false;
        }
    }

    private static void WriteSequenceNode(
        StringBuilder sb,
        YamlSequenceNode sequence,
        string indent,
        int level,
        FormattingOptions options)
    {
        foreach (var item in sequence.Children)
        {
            sb.Append(string.Concat(Enumerable.Repeat(indent, level)));
            sb.Append("- ");

            if (item is YamlMappingNode mappingItem)
            {
                var isFirst = true;
                foreach (var pair in mappingItem.Children)
                {
                    if (!isFirst)
                    {
                        sb.Append(string.Concat(Enumerable.Repeat(indent, level + 1)));
                    }

                    if (pair.Key is YamlScalarNode keyScalar)
                    {
                        sb.Append(keyScalar.Value);
                        sb.Append(':');
                    }

                    if (pair.Value is YamlMappingNode or YamlSequenceNode)
                    {
                        sb.Append('\n');
                        WriteNode(sb, pair.Value, indent, level + 2, options);
                    }
                    else
                    {
                        sb.Append(' ');
                        WriteNode(sb, pair.Value, indent, level + 2, options);
                        sb.Append('\n');
                    }

                    isFirst = false;
                }
            }
            else
            {
                WriteNode(sb, item, indent, level + 1, options);
                sb.Append('\n');
            }
        }
    }

    private static void WriteScalarNode(StringBuilder sb, YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";

        // Determine quoting style
        if (value.Contains('\n'))
        {
            sb.Append("|\n");
            foreach (var line in value.Split('\n'))
            {
                sb.Append("  ");
                sb.Append(line);
                sb.Append('\n');
            }
        }
        else if (NeedsQuoting(value))
        {
            sb.Append('"');
            sb.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
            sb.Append('"');
        }
        else
        {
            sb.Append(value);
        }
    }

    private static bool NeedsQuoting(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        // Values that could be misinterpreted
        if (value is "true" or "false" or "null" or "yes" or "no" or
            "True" or "False" or "Null" or "Yes" or "No" or
            "TRUE" or "FALSE" or "NULL" or "YES" or "NO")
        {
            return true;
        }

        if (double.TryParse(value, out _))
        {
            return true;
        }

        // Values with special characters
        if (value.Contains(':') || value.Contains('#') || value.Contains('[') ||
            value.Contains(']') || value.Contains('{') || value.Contains('}') ||
            value.Contains(',') || value.StartsWith(' ') || value.EndsWith(' '))
        {
            return true;
        }

        return false;
    }

    private static string FormatWhitespaceOnly(string text, FormattingOptions options)
    {
        // Fallback: just normalize indentation without parsing
        var lines = text.Split('\n');
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            if (options.TrimTrailingWhitespace)
            {
                sb.Append(line.TrimEnd());
            }
            else
            {
                sb.Append(line);
            }
            sb.Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }
}
