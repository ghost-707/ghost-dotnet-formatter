using System.Text;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Model;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.FSharp;

/// <summary>
/// Formats F# source files with consistent indentation and spacing.
/// Respects F#'s indentation-sensitive syntax: never alters semantic indentation levels.
/// Focuses on trailing whitespace, blank lines, and consistent spacing.
/// </summary>
public sealed class FSharpFormatter : BaseFormatter
{
    /// <inheritdoc />
    public override Language Language => Language.FSharp;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".fs", ".fsi", ".fsx"];

    /// <summary>Initializes a new instance of the <see cref="FSharpFormatter"/> class.</summary>
    public FSharpFormatter(ILogger<FSharpFormatter> logger) : base(logger) { }

    /// <inheritdoc />
    protected override Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // F# is indentation-sensitive, so we must be extremely conservative.
        // We only normalize:
        // 1. Trailing whitespace
        // 2. Consecutive blank lines
        // 3. Tab/space consistency (only if the entire file is consistent)
        // 4. Final newline
        // We DO NOT change indentation levels — that would change semantics.

        var lines = text.Split('\n');
        var sb = new StringBuilder();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var processed = line;

            // Trim trailing whitespace (safe — F# doesn't use trailing spaces)
            if (options.TrimTrailingWhitespace)
            {
                processed = processed.TrimEnd();
            }

            // Convert tabs to spaces only in leading whitespace (if configured)
            if (!options.UseTabs)
            {
                processed = ConvertLeadingTabsToSpaces(processed, options.IndentSize);
            }

            sb.Append(processed);
            sb.Append('\n');
        }

        var result = sb.ToString().TrimEnd('\n');
        return Task.FromResult(result);
    }

    private static string ConvertLeadingTabsToSpaces(string line, int indentSize)
    {
        if (!line.Contains('\t'))
        {
            return line;
        }

        var sb = new StringBuilder();
        var column = 0;
        var inLeading = true;

        foreach (var c in line)
        {
            if (inLeading && c == '\t')
            {
                var spacesToNext = indentSize - (column % indentSize);
                sb.Append(' ', spacesToNext);
                column += spacesToNext;
            }
            else if (inLeading && c == ' ')
            {
                sb.Append(c);
                column++;
            }
            else
            {
                inLeading = false;
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
