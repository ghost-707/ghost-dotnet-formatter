namespace GhostFormatter.Abstractions.Enums;

/// <summary>Namespace declaration style for C#.</summary>
public enum NamespaceStyle
{
    /// <summary>File-scoped namespace (C# 10+).</summary>
    FileScoped,

    /// <summary>Block-scoped namespace (traditional).</summary>
    BlockScoped,

    /// <summary>Preserve existing style.</summary>
    Preserve,
}
