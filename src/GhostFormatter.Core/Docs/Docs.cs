namespace GhostFormatter.Core.Docs;

/// <summary>
/// The Doc IR. This is a small, closed algebra of "document" primitives, modelled directly
/// on the intermediate representation used by Prettier / CSharpier. A Doc describes *what*
/// should be printed and *where it is allowed to break*; it says nothing about column widths
/// or actual newlines - that decision is made later, by <c>DocPrinter</c>, based on how much
/// horizontal space is available at print time.
///
/// This is a deliberately closed set of record types (a discriminated union). Every new case
/// added here must be handled by three places: DocPrinter.Print, DocPrinter.Fits and
/// DocPrinter.PropagateBreaks. The original IR (StringDoc/ConcatDoc/LineDoc/IndentDoc/GroupDoc)
/// could not express:
///   - "print this token only if the enclosing group broke" (trailing commas, optional
///     parentheses) -> IfBreakDoc
///   - "indent this content only if the enclosing group broke" -> IndentIfBreakDoc
///   - "defer this content (a trailing // comment) to the end of the current line, regardless
///     of where in the middle of the line it was produced" -> LineSuffixDoc / LineSuffixBoundaryDoc
///   - "force every group that contains me to break, even though I am not myself a line" ->
///     BreakParentDoc (hardline is defined in terms of this)
///   - "this is a real, already-formatted newline with no indentation" (the body of a verbatim
///     or raw string literal, or a block comment) -> LineKind.Literal
/// Every one of these is used by the visitor for a specific, real C# construct - see the
/// "Doc IR extensions" section of the written response for the mapping.
/// </summary>
public abstract record Doc
{
    public static implicit operator Doc(string value) => new StringDoc(value ?? string.Empty);
}

/// <summary>Literal text with no line breaks embedded in it. Never put '\n' inside a StringDoc -
/// use LiteralLine (via Docs.FromRawText) so the printer can track column position correctly.</summary>
public sealed record StringDoc(string Value) : Doc;

public sealed record ConcatDoc(IReadOnlyList<Doc> Parts) : Doc;

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

public sealed record LineDoc(LineKind Kind) : Doc;

public sealed record IndentDoc(Doc Content) : Doc;

/// <summary>
/// A breakable unit. The printer first tries to print Content in "flat" mode (all Line docs
/// collapse to spaces/nothing); if it doesn't fit in the remaining width - or ShouldBreak is
/// set, or Content contains a hard break anywhere (see PropagateBreaks) - Content is printed
/// in "break" mode instead (all Line docs become real newlines).
///
/// Groups nest independently: a broken outer group does NOT force an inner group to break
/// unless the inner group's own content doesn't fit or itself contains a hard break. This is
/// the single most important correctness property for "Group A / Group B / Group C" nesting
/// requested in the brief, and it is exactly the property the original DocPrinter got wrong
/// (it OR'd the parent's "broken" flag into every descendant unconditionally).
/// </summary>
public sealed record GroupDoc(Doc Content, bool ShouldBreak = false, string? Id = null) : Doc;

/// <summary>Prints BreakContents if the referenced group (by Id, or the nearest enclosing group
/// if Id is null) is in break mode, otherwise FlatContents. Used for trailing commas, optional
/// parentheses around a single lambda parameter, etc.</summary>
public sealed record IfBreakDoc(Doc BreakContents, Doc FlatContents, string? GroupId = null) : Doc;

/// <summary>Indents Content by one level only if the referenced group is broken. Used so that a
/// wrapped binary-expression continuation or a wrapped arrow-body only gains indentation when it
/// actually wraps.</summary>
public sealed record IndentIfBreakDoc(Doc Content, string? GroupId = null) : Doc;

/// <summary>Buffers Content and prints it immediately before the next real newline, instead of at
/// the point it occurs in the tree. This is what lets a trailing "// comment" attach to the end
/// of the current output line even though, structurally, more Doc content for that same node
/// might still be queued after it.</summary>
public sealed record LineSuffixDoc(Doc Content) : Doc;

/// <summary>If there are any buffered line-suffixes, forces a hard line break here so they get
/// flushed before whatever comes next (used before closing a block, so a dangling trailing
/// comment can't leak past a '}').</summary>
public sealed record LineSuffixBoundaryDoc : Doc;

/// <summary>Forces every group that (transitively) contains this doc to break, without itself
/// printing anything. Hardline is defined as Concat(LineDoc(Hard), BreakParentDoc) so that a
/// hard line anywhere inside a group always forces that group - and every group containing it -
/// to break.</summary>
public sealed record BreakParentDoc : Doc;

/// <summary>Removes trailing spaces/tabs already written on the current output line. Used rarely,
/// mainly to keep otherwise-empty lines clean.</summary>
public sealed record TrimDoc : Doc;

public static class Docs
{
    public static Doc Concat(params Doc[] docs) => new ConcatDoc(docs);

    public static Doc Concat(IEnumerable<Doc> docs) => new ConcatDoc(docs.ToList());

    public static Doc Group(Doc content, bool shouldBreak = false, string? id = null) =>
        new GroupDoc(content, shouldBreak, id);

    public static Doc Group(params Doc[] docs) => new GroupDoc(new ConcatDoc(docs));

    public static Doc Indent(Doc content) => new IndentDoc(content);

    public static Doc Indent(params Doc[] docs) => new IndentDoc(new ConcatDoc(docs));

    public static Doc IfBreak(
        Doc breakContents,
        Doc? flatContents = null,
        string? groupId = null
    ) => new IfBreakDoc(breakContents, flatContents ?? (Doc)"", groupId);

    public static Doc IndentIfBreak(Doc content, string? groupId = null) =>
        new IndentIfBreakDoc(content, groupId);

    public static Doc LineSuffix(Doc content) => new LineSuffixDoc(content);

    public static Doc LineSuffixBoundary => new LineSuffixBoundaryDoc();

    public static Doc BreakParent => new BreakParentDoc();

    public static Doc Trim => new TrimDoc();

    public static Doc Line => new LineDoc(LineKind.Space);

    public static Doc SoftLine => new LineDoc(LineKind.Soft);

    /// <summary>A hard line always forces every enclosing group to break. It is a normal
    /// LineDoc(Hard) plus an explicit BreakParent so that propagation is driven from a single,
    /// explicit mechanism rather than a special case in PropagateBreaks for LineKind.Hard alone
    /// (a LiteralLine, for instance, is also a "real newline" but must NOT force enclosing groups
    /// to break the same way, since it can legitimately appear inside a flat-printed literal).</summary>
    public static Doc HardLine =>
        new ConcatDoc(new Doc[] { new LineDoc(LineKind.Hard), new BreakParentDoc() });

    /// <summary>Like HardLine, but a raw newline with no indentation and no propagation - used
    /// inside the body text of verbatim/raw string literals and block comments, where we are
    /// reproducing the developer's own original whitespace verbatim.</summary>
    public static Doc LiteralLine => new LineDoc(LineKind.Literal);

    /// <summary>True for a null reference or a StringDoc with an empty Value - the "nothing to
    /// render" sentinel used throughout the visitor to decide whether an optional section (e.g.
    /// a dangling comment before a closing brace) needs any surrounding separators at all.</summary>
    public static bool IsEmpty(Doc? doc) => doc is null or StringDoc { Value.Length: 0 };

    public static Doc Join(Doc separator, IEnumerable<Doc> docs)
    {
        var list = docs.ToList();
        if (list.Count == 0)
            return "";

        var result = new List<Doc>(list.Count * 2 - 1);
        for (var i = 0; i < list.Count; i++)
        {
            result.Add(list[i]);
            if (i < list.Count - 1)
                result.Add(separator);
        }

        return new ConcatDoc(result);
    }

    /// <summary>Splits raw multi-line text (e.g. the body of a verbatim/raw string literal, or a
    /// block comment) on '\n' and rejoins the pieces with LiteralLine, so the printer's column
    /// tracking stays correct and downstream Fits() calculations aren't corrupted by an embedded
    /// newline hiding inside a StringDoc.</summary>
    public static Doc FromRawText(string text)
    {
        if (text.Length == 0)
            return "";

        // Normalize \r\n and lone \r to \n first, we re-emit the platform newline in the printer.
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n');
        if (lines.Length == 1)
            return lines[0];

        var parts = new List<Doc>(lines.Length * 2 - 1);
        for (var i = 0; i < lines.Length; i++)
        {
            parts.Add(lines[i]);
            if (i < lines.Length - 1)
                parts.Add(LiteralLine);
        }

        return new ConcatDoc(parts);
    }
}
