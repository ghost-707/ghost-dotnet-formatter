namespace GhostFormatter.Abstractions.Enums;

/// <summary>
/// Severity levels for formatting diagnostics.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational message.</summary>
    Info,

    /// <summary>Warning — formatting may be incomplete.</summary>
    Warning,

    /// <summary>Error — formatting was aborted for safety.</summary>
    Error
}
