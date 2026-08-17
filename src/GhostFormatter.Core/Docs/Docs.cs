using GhostFormatter.Abstractions.Enums;
using GhostFormatter.Core.Modals;

namespace GhostFormatter.Core.Docs;

public static class Docs
{
    public static Doc Concat(params Doc[] docs) => new ConcatDoc(docs);

    public static Doc Concat(IEnumerable<Doc> docs) => new ConcatDoc([.. docs]);

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
    public static Doc HardLine => new ConcatDoc([new LineDoc(LineKind.Hard), new BreakParentDoc()]);

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
