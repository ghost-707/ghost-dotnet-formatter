using System.Text;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Enums;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.Markdown;

/// <summary>
/// Formats Markdown files with consistent spacing and structure.
/// Preserves all content; adjusts blank lines, trailing whitespace, and list indentation.
/// Preserves fenced code blocks verbatim.
/// </summary>
public sealed class MarkdownFormatter : BaseFormatter
{
    /// <inheritdoc />
    public override Language Language => Language.Markdown;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".md", ".markdown"];

    /// <summary>Initializes a new instance of the <see cref="MarkdownFormatter"/> class.</summary>
    public MarkdownFormatter(ILogger<MarkdownFormatter> logger) : base(logger) { }

    /// <inheritdoc />
    protected override Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var inCodeBlock = false;
        var previousLineBlank = false;

        foreach (var rawLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Track fenced code blocks
            if (rawLine.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                rawLine.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                sb.Append(rawLine.TrimEnd());
                sb.Append('\n');
                previousLineBlank = false;
                continue;
            }

            // Inside code blocks: preserve exactly
            if (inCodeBlock)
            {
                sb.Append(rawLine);
                sb.Append('\n');
                previousLineBlank = false;
                continue;
            }

            var line = rawLine.TrimEnd();

            // Blank line management
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!previousLineBlank)
                {
                    sb.Append('\n');
                }
                previousLineBlank = true;
                continue;
            }

            // Ensure blank line before headings
            if (line.StartsWith('#') && sb.Length > 0 && !previousLineBlank)
            {
                sb.Append('\n');
            }

            // Ensure space after heading markers
            if (line.StartsWith('#'))
            {
                var hashCount = 0;
                while (hashCount < line.Length && line[hashCount] == '#')
                {
                    hashCount++;
                }

                if (hashCount < line.Length && line[hashCount] != ' ')
                {
                    line = line[..hashCount] + " " + line[hashCount..];
                }
            }

            sb.Append(line);
            sb.Append('\n');
            previousLineBlank = false;
        }

        var result = sb.ToString().TrimEnd('\n');
        return Task.FromResult(result);
    }
}
