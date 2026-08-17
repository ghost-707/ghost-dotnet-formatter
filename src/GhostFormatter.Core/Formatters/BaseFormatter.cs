using System.Diagnostics;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Enums;
using GhostFormatter.Abstractions.Formatters;
using GhostFormatter.Abstractions.Model;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters;

/// <summary>
/// Base class for language formatters providing common functionality
/// such as timing, error handling, and line-ending normalization.
/// </summary>
public abstract class BaseFormatter : ILanguageFormatter
{
    /// <summary>Gets the logger instance.</summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public abstract Language Language { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Initializes a new instance of the <see cref="BaseFormatter"/> class.</summary>
    protected BaseFormatter(ILogger logger)
    {
        Logger = logger;
    }

    /// <inheritdoc />
    public async Task<FormattingResult> FormatAsync(
        FormattingRequest request,
        FormattingOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var sw = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Pre-flight validation
            var diagnostics = new List<FormattingDiagnostic>();
            if (!await ValidateInputAsync(request.Text, diagnostics, cancellationToken))
            {
                sw.Stop();
                return FormattingResult.Failed(request.Text, sw.Elapsed, diagnostics);
            }
            // Normalize line endings for processing
            var normalized = NormalizeLineEndings(request.Text);
            // Apply formatting
            var formatted = request.Selection.HasValue
                ? await FormatSelectionCoreAsync(
                    normalized,
                    request.Selection.Value,
                    options,
                    cancellationToken
                )
                : await FormatCoreAsync(normalized, options, cancellationToken);
            // Apply final normalizations
            formatted = ApplyFinalNormalizations(formatted, options);
            sw.Stop();
            if (string.Equals(request.Text, formatted, StringComparison.Ordinal))
            {
                return FormattingResult.Unchanged(request.Text, sw.Elapsed);
            }
            return FormattingResult.Succeeded(request.Text, formatted, sw.Elapsed, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.LogError(ex, "Formatting failed for {Language}", Language);
            return FormattingResult.Failed(
                request.Text,
                sw.Elapsed,
                [FormattingDiagnostic.Error($"Formatting failed: {ex.Message}", code: "GF0001")]
            );
        }
    }

    /// <inheritdoc />
    public virtual Task<bool> CanFormatAsync(
        string text,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(!string.IsNullOrEmpty(text));

    /// <summary>
    /// Core formatting logic to be implemented by each language formatter.
    /// </summary>
    protected abstract Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Formats a selection within a document. By default, formats the full document.
    /// Override for formatters that support selection-based formatting.
    /// </summary>
    protected virtual Task<string> FormatSelectionCoreAsync(
        string text,
        TextSpan selection,
        FormattingOptions options,
        CancellationToken cancellationToken
    ) => FormatCoreAsync(text, options, cancellationToken);

    /// <summary>
    /// Validates the input text before formatting.
    /// Override to add language-specific validation.
    /// </summary>
    protected virtual Task<bool> ValidateInputAsync(
        string text,
        List<FormattingDiagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(text))
        {
            diagnostics.Add(FormattingDiagnostic.Warning("Input text is empty.", code: "GF0002"));
            return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    /// <summary>Normalizes line endings to LF for processing.</summary>
    protected static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    /// <summary>Applies final normalizations: line endings, trailing whitespace, final newline.</summary>
    protected static string ApplyFinalNormalizations(string text, FormattingOptions options)
    {
        // Trim trailing whitespace from each line
        if (options.TrimTrailingWhitespace)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].TrimEnd();
            }
            text = string.Join('\n', lines);
        }
        // Collapse consecutive blank lines
        if (options.MaxConsecutiveBlankLines >= 0)
        {
            text = CollapseBlankLines(text, options.MaxConsecutiveBlankLines);
        }
        // Apply line ending style
        text = options.LineEnding switch
        {
            LineEndingStyle.CrLf => text.Replace("\n", "\r\n"),
            LineEndingStyle.Lf => text,
            LineEndingStyle.Preserve => text,
            _ => text,
        };
        // Final newline
        var newlineChar = options.LineEnding == LineEndingStyle.CrLf ? "\r\n" : "\n";
        if (options.InsertFinalNewline && !text.EndsWith(newlineChar, StringComparison.Ordinal))
        {
            text += newlineChar;
        }
        else if (
            !options.InsertFinalNewline && text.EndsWith(newlineChar, StringComparison.Ordinal)
        )
        {
            text = text.TrimEnd('\r', '\n');
        }
        return text;
    }

    private static string CollapseBlankLines(string text, int maxConsecutive)
    {
        var lines = text.Split('\n');
        var result = new List<string>(lines.Length);
        var consecutiveBlanks = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                consecutiveBlanks++;
                if (consecutiveBlanks <= maxConsecutive)
                {
                    result.Add(line);
                }
            }
            else
            {
                consecutiveBlanks = 0;
                result.Add(line);
            }
        }
        return string.Join('\n', result);
    }

    /// <summary>Creates an indentation string based on options.</summary>
    protected static string MakeIndent(FormattingOptions options, int level)
    {
        if (options.UseTabs)
        {
            return new string('\t', level);
        }
        return new string(' ', level * options.IndentSize);
    }
}
