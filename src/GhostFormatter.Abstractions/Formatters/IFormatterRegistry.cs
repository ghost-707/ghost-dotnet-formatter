using GhostFormatter.Abstractions.Model;

namespace GhostFormatter.Abstractions.Formatters;

/// <summary>
/// Registry for language-specific formatters.
/// Resolves the appropriate formatter for a given language or file extension.
/// </summary>
public interface IFormatterRegistry
{
    /// <summary>
    /// Gets the formatter for the specified language.
    /// </summary>
    /// <param name="language">The target language.</param>
    /// <returns>The formatter, or <c>null</c> if no formatter is registered for the language.</returns>
    ILanguageFormatter? GetFormatter(Language language);

    /// <summary>
    /// Gets the formatter for the specified file extension.
    /// </summary>
    /// <param name="extension">The file extension including the leading dot (e.g., ".cs").</param>
    /// <returns>The formatter, or <c>null</c> if no formatter handles the extension.</returns>
    ILanguageFormatter? GetFormatterByExtension(string extension);

    /// <summary>
    /// Registers a language formatter.
    /// </summary>
    /// <param name="formatter">The formatter to register.</param>
    void Register(ILanguageFormatter formatter);

    /// <summary>
    /// Gets all registered formatters.
    /// </summary>
    IReadOnlyList<ILanguageFormatter> GetAll();
}
