namespace GhostFormatter.Abstractions.Enums;

/// <summary>Trailing comma behavior.</summary>
public enum TrailingCommaStyle
{
    /// <summary>Do not add trailing commas.</summary>
    None,

    /// <summary>Add trailing commas in multi-line constructs.</summary>
    MultiLine,

    /// <summary>Preserve existing trailing commas.</summary>
    Preserve
}
