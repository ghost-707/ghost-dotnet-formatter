namespace GhostFormatter.Abstractions.Enums;

/// <summary>Brace placement style.</summary>
public enum BraceStyle
{
    /// <summary>Opening brace on a new line (C# default).</summary>
    Allman,

    /// <summary>Opening brace on the same line (K&amp;R style).</summary>
    KAndR,

    /// <summary>Opening brace on the same line, closing brace on its own line.</summary>
    Stroustrup
}
