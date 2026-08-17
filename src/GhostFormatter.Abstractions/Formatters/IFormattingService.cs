using GhostFormatter.Abstractions.Enums;
using GhostFormatter.Abstractions.Model;

namespace GhostFormatter.Abstractions.Formatters;

/// <summary>
/// The primary formatting service that coordinates language detection, option resolution,
/// and formatter dispatch.
/// </summary>
public interface IFormattingService
{
    /// <summary>
    /// Formats text according to the detected or specified language.
    /// </summary>
    /// <param name="request">The formatting request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="FormattingResult"/>.</returns>
    Task<FormattingResult> FormatAsync(FormattingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Formats a file on disk, reading and writing the file contents.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="FormattingResult"/>.</returns>
    Task<FormattingResult> FormatFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the language from a file path.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The detected language.</returns>
    Language DetectLanguage(string filePath);
}
