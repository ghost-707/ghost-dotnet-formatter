namespace GhostFormatter.Core.Modals;

public enum LineKind
{
    /// <summary>Space when flat, newline+indent when broken.</summary>
    Space,

    /// <summary>Nothing when flat, newline+indent when broken.</summary>
    Soft,

    /// <summary>Always a newline+indent. Forces every enclosing group to break (see BreakParentDoc).</summary>
    Hard,

    /// <summary>A raw '\n' with NO indentation inserted, and does not by itself force enclosing
    /// groups to break the way Hard does implicitly through the concat it's embedded in - used
    /// for the interior of verbatim/raw string literals and multi-line comments, where the
    /// original text's own whitespace must be reproduced exactly.</summary>
    Literal,
}
