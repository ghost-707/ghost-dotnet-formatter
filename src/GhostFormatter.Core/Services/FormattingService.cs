using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Enums;
using GhostFormatter.Abstractions.Formatters;
using GhostFormatter.Abstractions.Model;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Services;

/// <summary>
/// Primary formatting service that coordinates language detection, option resolution,
/// and formatter dispatch. Serves as the main entry point for all formatting operations.
/// </summary>
public sealed class FormattingService : IFormattingService
{
    private readonly IFormatterRegistry _registry;
    private readonly IOptionsProvider _optionsProvider;
    private readonly LanguageDetector _languageDetector;
    private readonly ILogger<FormattingService> _logger;

    /// <summary>Initializes a new instance of the <see cref="FormattingService"/> class.</summary>
    public FormattingService(
        IFormatterRegistry registry,
        IOptionsProvider optionsProvider,
        LanguageDetector languageDetector,
        ILogger<FormattingService> logger)
    {
        _registry = registry;
        _optionsProvider = optionsProvider;
        _languageDetector = languageDetector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FormattingResult> FormatAsync(
        FormattingRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Resolve language
        var language = request.Language;
        if (language == Language.Unknown && request.FilePath is not null)
        {
            language = _languageDetector.DetectFromPath(request.FilePath);
        }

        if (language == Language.Unknown)
        {
            language = _languageDetector.DetectFromContent(request.Text);
        }

        // Find formatter
        var formatter = _registry.GetFormatter(language);
        if (formatter is null)
        {
            sw.Stop();
            _logger.LogWarning("No formatter registered for language {Language}", language);
            return FormattingResult.Failed(
                request.Text,
                sw.Elapsed,
                [FormattingDiagnostic.Warning($"No formatter available for language '{language}'.", code: "GF0003")]);
        }

        // Resolve options
        var options = await _optionsProvider.GetOptionsAsync(request.FilePath, cancellationToken);

        _logger.LogDebug("Formatting {Language} ({Length} chars)", language, request.Text.Length);

        // Format
        var result = await formatter.FormatAsync(request, options, cancellationToken);

        _logger.LogDebug("Formatting completed in {Elapsed}ms (modified: {WasModified})",
            result.Elapsed.TotalMilliseconds, result.WasModified);

        return result;
    }

    /// <inheritdoc />
    public async Task<FormattingResult> FormatFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return FormattingResult.Failed(
                string.Empty,
                TimeSpan.Zero,
                [FormattingDiagnostic.Error($"File not found: {filePath}", code: "GF0004")]);
        }

        var text = await File.ReadAllTextAsync(filePath, cancellationToken);
        var language = DetectLanguage(filePath);

        var request = new FormattingRequest
        {
            Text = text,
            Language = language,
            FilePath = filePath,
            Scope = FormattingScope.Document
        };

        var result = await FormatAsync(request, cancellationToken);

        // Write back if formatting succeeded and text was modified
        if (result.Success && result.WasModified && result.FormattedText is not null)
        {
            await File.WriteAllTextAsync(filePath, result.FormattedText, cancellationToken);
            _logger.LogInformation("Formatted and saved: {FilePath}", filePath);
        }

        return result;
    }

    /// <inheritdoc />
    public Language DetectLanguage(string filePath) =>
        _languageDetector.DetectFromPath(filePath);
}
