namespace GhostFormatter.Abstractions.Model;

/// <summary>
/// Represents the outcome of a formatting operation.
/// </summary>
public sealed class FormattingResult
{
    /// <summary>Gets the formatted text, or <c>null</c> if formatting failed.</summary>
    public string? FormattedText { get; }

    /// <summary>Gets a value indicating whether formatting succeeded.</summary>
    public bool Success { get; }

    /// <summary>Gets the original (input) text.</summary>
    public string OriginalText { get; }

    /// <summary>Gets a value indicating whether the text was modified.</summary>
    public bool WasModified => Success && !string.Equals(OriginalText, FormattedText, StringComparison.Ordinal);

    /// <summary>Gets diagnostics produced during formatting.</summary>
    public IReadOnlyList<FormattingDiagnostic> Diagnostics { get; }

    /// <summary>Gets the elapsed time for the formatting operation.</summary>
    public TimeSpan Elapsed { get; }

    private FormattingResult(
        string originalText,
        string? formattedText,
        bool success,
        IReadOnlyList<FormattingDiagnostic> diagnostics,
        TimeSpan elapsed)
    {
        OriginalText = originalText;
        FormattedText = formattedText;
        Success = success;
        Diagnostics = diagnostics;
        Elapsed = elapsed;
    }

    /// <summary>Creates a successful result.</summary>
    public static FormattingResult Succeeded(
        string originalText,
        string formattedText,
        TimeSpan elapsed,
        IReadOnlyList<FormattingDiagnostic>? diagnostics = null)
    {
        return new FormattingResult(
            originalText,
            formattedText,
            success: true,
            diagnostics ?? Array.Empty<FormattingDiagnostic>(),
            elapsed);
    }

    /// <summary>Creates a failed result.</summary>
    public static FormattingResult Failed(
        string originalText,
        TimeSpan elapsed,
        IReadOnlyList<FormattingDiagnostic> diagnostics)
    {
        return new FormattingResult(
            originalText,
            formattedText: null,
            success: false,
            diagnostics,
            elapsed);
    }

    /// <summary>Creates a result where no changes were needed.</summary>
    public static FormattingResult Unchanged(string text, TimeSpan elapsed)
    {
        return new FormattingResult(
            text,
            text,
            success: true,
            Array.Empty<FormattingDiagnostic>(),
            elapsed);
    }
}
