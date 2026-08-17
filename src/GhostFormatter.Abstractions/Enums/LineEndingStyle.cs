namespace GhostFormatter.Abstractions.Enums;

/// <summary>Line ending style.</summary>
public enum LineEndingStyle
{
    /// <summary>Unix-style line feed.</summary>
    Lf,

    /// <summary>Windows-style carriage return + line feed.</summary>
    CrLf,

    /// <summary>Preserve existing line endings.</summary>
    Preserve,
}
