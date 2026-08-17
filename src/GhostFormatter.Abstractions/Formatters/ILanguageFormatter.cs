using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Enums;
using GhostFormatter.Abstractions.Model;

namespace GhostFormatter.Abstractions.Formatters;

/// <summary>
/// Defines the contract for a language-specific formatter.
/// Each supported language implements this interface.
/// </summary>
public interface ILanguageFormatter
{
    /// <summary>Gets the language this formatter handles.</summary>
    Language Language { get; }

    /// <summary>Gets the file extensions this formatter supports (e.g., ".cs", ".csx").</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Formats the source text according to the provided options.
    /// </summary>
    /// <param name="request">The formatting request.</param>
    /// <param name="options">The resolved formatting options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="FormattingResult"/> describing the outcome.</returns>
    Task<FormattingResult> FormatAsync(
        FormattingRequest request,
        FormattingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the formatter can process the given text without errors.
    /// Useful for pre-flight checks before formatting.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><c>true</c> if the text can be safely formatted; otherwise <c>false</c>.</returns>
    Task<bool> CanFormatAsync(string text, CancellationToken cancellationToken = default);
}
