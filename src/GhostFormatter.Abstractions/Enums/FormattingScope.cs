namespace GhostFormatter.Abstractions.Enums;

/// <summary>
/// Defines the scope of a formatting operation.
/// </summary>
public enum FormattingScope
{
    /// <summary>Format a text selection within a document.</summary>
    Selection,

    /// <summary>Format an entire document.</summary>
    Document,

    /// <summary>Format all documents in a project.</summary>
    Project,

    /// <summary>Format all documents in a solution.</summary>
    Solution
}
