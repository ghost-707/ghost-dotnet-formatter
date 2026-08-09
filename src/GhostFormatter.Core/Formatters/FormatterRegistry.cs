using GhostFormatter.Abstractions.Formatters;
using GhostFormatter.Abstractions.Model;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters;

/// <summary>
/// Central registry for language-specific formatters.
/// Auto-populates from all <see cref="ILanguageFormatter"/> instances registered via DI.
/// </summary>
public sealed class FormatterRegistry : IFormatterRegistry
{
    private readonly Dictionary<Language, ILanguageFormatter> _byLanguage = new();
    private readonly Dictionary<string, ILanguageFormatter> _byExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FormatterRegistry> _logger;

    /// <summary>Initializes a new instance of the <see cref="FormatterRegistry"/> class.</summary>
    public FormatterRegistry(
        IEnumerable<ILanguageFormatter> formatters,
        ILogger<FormatterRegistry> logger)
    {
        _logger = logger;

        foreach (var formatter in formatters)
        {
            Register(formatter);
        }

        _logger.LogInformation("FormatterRegistry initialized with {Count} formatters", _byLanguage.Count);
    }

    /// <inheritdoc />
    public ILanguageFormatter? GetFormatter(Language language)
    {
        _byLanguage.TryGetValue(language, out var formatter);
        return formatter;
    }

    /// <inheritdoc />
    public ILanguageFormatter? GetFormatterByExtension(string extension)
    {
        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        _byExtension.TryGetValue(extension, out var formatter);
        return formatter;
    }

    /// <inheritdoc />
    public void Register(ILanguageFormatter formatter)
    {
        _byLanguage[formatter.Language] = formatter;

        foreach (var ext in formatter.SupportedExtensions)
        {
            _byExtension[ext] = formatter;
        }

        _logger.LogDebug("Registered formatter for {Language} ({Extensions})",
            formatter.Language,
            string.Join(", ", formatter.SupportedExtensions));
    }

    /// <inheritdoc />
    public IReadOnlyList<ILanguageFormatter> GetAll() => _byLanguage.Values.ToList().AsReadOnly();
}
