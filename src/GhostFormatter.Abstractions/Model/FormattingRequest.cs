namespace GhostFormatter.Abstractions.Model;

/// <summary>
/// Encapsulates all information needed for a formatting operation.
/// </summary>
public sealed class FormattingRequest
{
    /// <summary>Gets the source text to format.</summary>
    public required string Text { get; init; }

    /// <summary>Gets the language of the source text.</summary>
    public required Language Language { get; init; }

    /// <summary>Gets the file path, if available (used for diagnostics and config resolution).</summary>
    public string? FilePath { get; init; }

    /// <summary>Gets the optional text span to format. When <c>null</c>, the entire document is formatted.</summary>
    public TextSpan? Selection { get; init; }

    /// <summary>Gets the formatting scope.</summary>
    public FormattingScope Scope { get; init; } = FormattingScope.Document;
}
