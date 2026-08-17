using GhostFormatter.Abstractions.Enums;

namespace GhostFormatter.Abstractions.Model;

/// <summary>
/// Represents a diagnostic message produced during formatting.
/// </summary>
/// <param name="Severity">The severity level.</param>
/// <param name="Message">A human-readable diagnostic message.</param>
/// <param name="Line">The 1-based line number, or <c>null</c> if not applicable.</param>
/// <param name="Column">The 1-based column number, or <c>null</c> if not applicable.</param>
/// <param name="Code">A diagnostic code for categorization.</param>
public sealed record FormattingDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    int? Line = null,
    int? Column = null,
    string? Code = null
)
{
    /// <summary>Creates an error diagnostic.</summary>
    public static FormattingDiagnostic Error(
        string message,
        int? line = null,
        int? column = null,
        string? code = null
    ) => new(DiagnosticSeverity.Error, message, line, column, code);

    /// <summary>Creates a warning diagnostic.</summary>
    public static FormattingDiagnostic Warning(
        string message,
        int? line = null,
        int? column = null,
        string? code = null
    ) => new(DiagnosticSeverity.Warning, message, line, column, code);

    /// <summary>Creates an informational diagnostic.</summary>
    public static FormattingDiagnostic Info(
        string message,
        int? line = null,
        int? column = null,
        string? code = null
    ) => new(DiagnosticSeverity.Info, message, line, column, code);
}
