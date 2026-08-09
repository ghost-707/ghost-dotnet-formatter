namespace GhostFormatter.Abstractions.Configuration;

/// <summary>
/// Provides formatting options, merging defaults with .editorconfig and user settings.
/// </summary>
public interface IOptionsProvider
{
    /// <summary>
    /// Resolves the effective formatting options for the given file path.
    /// Merges global defaults, .editorconfig settings, and user overrides.
    /// </summary>
    /// <param name="filePath">The file path for context-sensitive resolution, or <c>null</c> for defaults.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved formatting options.</returns>
    Task<FormattingOptions> GetOptionsAsync(string? filePath, CancellationToken cancellationToken = default);
}
