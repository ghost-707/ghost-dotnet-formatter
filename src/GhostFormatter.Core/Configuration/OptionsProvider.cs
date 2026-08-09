using GhostFormatter.Abstractions.Configuration;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Configuration;

/// <summary>
/// Resolves formatting options by merging global defaults with .editorconfig settings.
/// </summary>
public sealed class OptionsProvider : IOptionsProvider
{
    private readonly FormattingOptions _defaults = new();
    private readonly ILogger<OptionsProvider> _logger;
    private readonly EditorConfigParser _editorConfigParser;

    /// <summary>Initializes a new instance of the <see cref="OptionsProvider"/> class.</summary>
    public OptionsProvider(ILogger<OptionsProvider> logger)
    {
        _logger = logger;
        _editorConfigParser = new EditorConfigParser(logger);
    }

    /// <inheritdoc />
    public async Task<FormattingOptions> GetOptionsAsync(string? filePath, CancellationToken cancellationToken = default)
    {
        var options = _defaults.Clone();

        if (filePath is not null)
        {
            try
            {
                var editorConfigOptions = await _editorConfigParser.ParseAsync(filePath, cancellationToken);
                MergeEditorConfig(options, editorConfigOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse .editorconfig for {FilePath}, using defaults", filePath);
            }
        }

        return options;
    }

    private static void MergeEditorConfig(FormattingOptions target, EditorConfigSettings source)
    {
        if (source.IndentSize.HasValue)
        {
            target.IndentSize = source.IndentSize.Value;
        }

        if (source.IndentStyle.HasValue)
        {
            target.UseTabs = source.IndentStyle.Value == IndentStyleValue.Tab;
        }

        if (source.EndOfLine.HasValue)
        {
            target.LineEnding = source.EndOfLine.Value switch
            {
                EndOfLineValue.Lf => LineEndingStyle.Lf,
                EndOfLineValue.CrLf => LineEndingStyle.CrLf,
                _ => LineEndingStyle.Lf
            };
        }

        if (source.InsertFinalNewline.HasValue)
        {
            target.InsertFinalNewline = source.InsertFinalNewline.Value;
        }

        if (source.TrimTrailingWhitespace.HasValue)
        {
            target.TrimTrailingWhitespace = source.TrimTrailingWhitespace.Value;
        }

        if (source.MaxLineLength.HasValue)
        {
            target.MaxLineLength = source.MaxLineLength.Value;
        }
    }
}
