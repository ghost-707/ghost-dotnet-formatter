using System;
using System.Collections.Generic;
using System.Text;
using GhostFormatter.Core.Docs;

namespace GhostFormatter.Core.Printer;

public sealed record DocPrinterOptions(
    int PrintWidth = 100,
    int IndentSize = 4,
    bool UseTabs = false
)
{
    public static readonly DocPrinterOptions Default = new();
}

internal enum Mode
{
    Flat,
    Break,
}

internal readonly record struct Cmd(int Indent, Mode Mode, Doc Doc);

/// <summary>
/// A from-scratch, Prettier-compatible implementation of printDocToString. This replaces the
/// original DocPrinter, whose algorithm had three separate correctness bugs (see the written
/// audit):
///
///   1. Fits() ignored the `currentCommands` parameter entirely, so it never accounted for what
///      content follows a group on the same line - it could report "fits" for a group whose
///      trailing content actually overflows the line.
///   2. GroupDoc resolution OR'd the *parent's* broken flag into the child (`broken ||
///      !Fits(...)`), which forces every descendant group to break as soon as any ancestor
///      breaks. That is precisely the nested-group requirement the brief calls out as broken:
///      "Group A / Group B / Group C, where breaking B does not incorrectly force C to break."
///      A group's mode must be decided independently, purely from whether *its own* content
///      fits in the *remaining* width.
///   3. There was no break-propagation pass, so a hard line buried inside a group's content
///      (e.g. a multi-statement lambda body passed as a call argument) would not force that
///      group - or its own ancestors - to break; the group would still attempt (and fail) to
///      measure itself as flat.
///
/// This implementation fixes all three: each GroupDoc's mode is resolved independently via
/// Fits(), Fits() consults the remainder of the command stack, and PropagateBreaks walks the
/// tree once up front to mark every group that transitively contains a hard break.
/// </summary>
public sealed class DocPrinter
{
    private readonly DocPrinterOptions _options;
    private readonly string _indentUnit;

    public DocPrinter(DocPrinterOptions options)
    {
        _options = options;
        _indentUnit = options.UseTabs ? "\t" : new string(' ', options.IndentSize);
    }

    public DocPrinter(int printWidth = 100, int indentSize = 4, bool useTabs = false)
        : this(new DocPrinterOptions(printWidth, indentSize, useTabs)) { }

    public string Print(Doc root)
    {
        root = PropagateBreaks(root);

        var sb = new StringBuilder();
        var pos = 0;
        var shouldRemeasure = true;
        var groupModeMap = new Dictionary<string, Mode>();
        var lineSuffixes = new List<Cmd>();

        var cmds = new Stack<Cmd>();
        cmds.Push(new Cmd(0, Mode.Break, root));

        while (cmds.Count > 0 || lineSuffixes.Count > 0)
        {
            if (cmds.Count == 0)
            {
                for (var i = lineSuffixes.Count - 1; i >= 0; i--)
                    cmds.Push(lineSuffixes[i]);
                lineSuffixes.Clear();
                continue;
            }

            var (indent, mode, doc) = cmds.Pop();

            switch (doc)
            {
                case StringDoc(var value):
                    if (value.Length > 0)
                    {
                        sb.Append(value);
                        pos += value.Length;
                    }
                    break;

                case ConcatDoc(var parts):
                    for (var i = parts.Count - 1; i >= 0; i--)
                        cmds.Push(new Cmd(indent, mode, parts[i]));
                    break;

                case IndentDoc(var content):
                    cmds.Push(new Cmd(indent + 1, mode, content));
                    break;

                case GroupDoc group:
                {
                    switch (mode)
                    {
                        case Mode.Flat when !shouldRemeasure:
                            cmds.Push(
                                new Cmd(
                                    indent,
                                    group.ShouldBreak ? Mode.Break : Mode.Flat,
                                    group.Content
                                )
                            );
                            break;

                        default:
                        {
                            shouldRemeasure = false;
                            var flatCmd = new Cmd(indent, Mode.Flat, group.Content);
                            var resolvedMode =
                                !group.ShouldBreak
                                && Fits(
                                    flatCmd,
                                    cmds,
                                    _options.PrintWidth - pos,
                                    groupModeMap,
                                    lineSuffixes.Count > 0
                                )
                                    ? Mode.Flat
                                    : Mode.Break;

                            if (group.Id != null)
                                groupModeMap[group.Id] = resolvedMode;

                            cmds.Push(new Cmd(indent, resolvedMode, group.Content));
                            break;
                        }
                    }

                    break;
                }

                case LineDoc(var kind):
                {
                    if (kind == LineKind.Literal)
                    {
                        TrimTrailingWhitespace(sb);
                        sb.Append('\n');
                        pos = 0;
                        shouldRemeasure = true;
                        break;
                    }

                    if (mode == Mode.Flat && kind != LineKind.Hard)
                    {
                        if (kind == LineKind.Space)
                        {
                            sb.Append(' ');
                            pos += 1;
                        }
                        // Soft: nothing when flat.
                        break;
                    }

                    if (lineSuffixes.Count > 0)
                    {
                        cmds.Push(new Cmd(indent, mode, doc));
                        for (var i = lineSuffixes.Count - 1; i >= 0; i--)
                            cmds.Push(lineSuffixes[i]);
                        lineSuffixes.Clear();
                        break;
                    }

                    TrimTrailingWhitespace(sb);
                    sb.Append('\n');
                    if (indent > 0)
                    {
                        var indentStr = GetIndentString(indent);
                        sb.Append(indentStr);
                        pos = indentStr.Length;
                    }
                    else
                    {
                        pos = 0;
                    }

                    break;
                }

                case IfBreakDoc ifBreak:
                {
                    var groupMode =
                        ifBreak.GroupId != null
                            ? groupModeMap.GetValueOrDefault(ifBreak.GroupId, Mode.Flat)
                            : mode;
                    var contents =
                        groupMode == Mode.Break ? ifBreak.BreakContents : ifBreak.FlatContents;
                    cmds.Push(new Cmd(indent, mode, contents));
                    break;
                }

                case IndentIfBreakDoc indentIfBreak:
                {
                    var groupMode =
                        indentIfBreak.GroupId != null
                            ? groupModeMap.GetValueOrDefault(indentIfBreak.GroupId, Mode.Flat)
                            : mode;
                    cmds.Push(
                        groupMode == Mode.Break
                            ? new Cmd(indent + 1, mode, indentIfBreak.Content)
                            : new Cmd(indent, mode, indentIfBreak.Content)
                    );
                    break;
                }

                case LineSuffixDoc lineSuffix:
                    lineSuffixes.Add(new Cmd(indent, mode, lineSuffix.Content));
                    break;

                case LineSuffixBoundaryDoc:
                    if (lineSuffixes.Count > 0)
                        cmds.Push(new Cmd(indent, mode, new LineDoc(LineKind.Hard)));
                    break;

                case BreakParentDoc:
                    // Handled entirely by PropagateBreaks; no-op at print time.
                    break;

                case TrimDoc:
                    pos -= TrimTrailingWhitespace(sb);
                    break;

                default:
                    throw new NotSupportedException($"Unhandled Doc type: {doc.GetType().Name}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Determines whether `next` (plus everything already queued after it in `restCommands`,
    /// up to the first line break that would actually terminate the current output line) fits
    /// within `width` columns. This mirrors Prettier's `fits` function, including consulting the
    /// remainder of the command stack - the single most important fix over the original
    /// implementation, which measured `content` in total isolation.
    /// </summary>
    private static bool Fits(
        Cmd next,
        Stack<Cmd> restCommands,
        int width,
        Dictionary<string, Mode> groupModeMap,
        bool hasLineSuffix
    )
    {
        var restIdx = 0;
        var rest = restCommands.ToArray(); // Stack<T>.ToArray() is top-first (pop order).
        var cmds = new Stack<Cmd>();
        cmds.Push(next);

        while (width >= 0)
        {
            if (cmds.Count == 0)
            {
                if (restIdx >= rest.Length)
                    return true;
                cmds.Push(rest[restIdx]);
                restIdx++;
                continue;
            }

            var (indent, mode, doc) = cmds.Pop();

            switch (doc)
            {
                case StringDoc(var value):
                    width -= value.Length;
                    break;

                case ConcatDoc(var parts):
                    for (var i = parts.Count - 1; i >= 0; i--)
                        cmds.Push(new Cmd(indent, mode, parts[i]));
                    break;

                case IndentDoc(var content):
                    cmds.Push(new Cmd(indent + 1, mode, content));
                    break;

                case GroupDoc group:
                {
                    var groupMode = group.ShouldBreak ? Mode.Break : mode;
                    cmds.Push(new Cmd(indent, groupMode, group.Content));
                    break;
                }

                case LineDoc(var kind):
                    if (kind == LineKind.Literal)
                        return true;
                    if (mode == Mode.Break || kind == LineKind.Hard)
                        return true;
                    if (kind == LineKind.Space)
                        width -= 1;
                    break;

                case IfBreakDoc ifBreak:
                {
                    var groupMode =
                        ifBreak.GroupId != null
                            ? groupModeMap.GetValueOrDefault(ifBreak.GroupId, Mode.Flat)
                            : mode;
                    var contents =
                        groupMode == Mode.Break ? ifBreak.BreakContents : ifBreak.FlatContents;
                    cmds.Push(new Cmd(indent, mode, contents));
                    break;
                }

                case IndentIfBreakDoc indentIfBreak:
                {
                    var groupMode =
                        indentIfBreak.GroupId != null
                            ? groupModeMap.GetValueOrDefault(indentIfBreak.GroupId, Mode.Flat)
                            : mode;
                    cmds.Push(
                        groupMode == Mode.Break
                            ? new Cmd(indent + 1, mode, indentIfBreak.Content)
                            : new Cmd(indent, mode, indentIfBreak.Content)
                    );
                    break;
                }

                case LineSuffixDoc:
                    hasLineSuffix = true;
                    break;

                case LineSuffixBoundaryDoc:
                    if (hasLineSuffix)
                        return false;
                    break;

                case BreakParentDoc:
                    break;

                case TrimDoc:
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Single bottom-up pass that marks every GroupDoc which transitively contains a BreakParentDoc
    /// (i.e. a HardLine) as ShouldBreak = true, and propagates that fact to every ancestor group as
    /// well. Without this pass, a group is only ever forced to break by *failing to fit*; a hard
    /// line nested three groups deep (e.g. a multi-statement lambda body inside an argument list
    /// inside a chained call) would otherwise be measured as flat and Fits() would see a hard line
    /// kind and return true (fits "up to here"), silently producing wrong, non-idempotent output.
    /// </summary>
    private static Doc PropagateBreaks(Doc doc) => PropagateCore(doc).Doc;

    private static (Doc Doc, bool Breaks) PropagateCore(Doc doc)
    {
        switch (doc)
        {
            case StringDoc:
                return (doc, false);

            case ConcatDoc(var parts):
            {
                var newParts = new Doc[parts.Count];
                var breaks = false;
                for (var i = 0; i < parts.Count; i++)
                {
                    var (d, b) = PropagateCore(parts[i]);
                    newParts[i] = d;
                    breaks |= b;
                }

                return (new ConcatDoc(newParts), breaks);
            }

            case IndentDoc(var content):
            {
                var (d, b) = PropagateCore(content);
                return (new IndentDoc(d), b);
            }

            case LineDoc(var kind):
                // A bare hard LineDoc (outside of Docs.HardLine's Concat-with-BreakParent) still
                // needs to force its parent, in case a caller constructed one directly.
                return (doc, kind == LineKind.Hard);

            case GroupDoc group:
            {
                var (content, contentBreaks) = PropagateCore(group.Content);
                var shouldBreak = group.ShouldBreak || contentBreaks;
                return (new GroupDoc(content, shouldBreak, group.Id), shouldBreak);
            }

            case IfBreakDoc ifBreak:
            {
                var (b, _) = PropagateCore(ifBreak.BreakContents);
                var (f, _) = PropagateCore(ifBreak.FlatContents);
                // Deliberately does not propagate: only one branch is ever printed, so neither
                // branch should be able to force an *outer* group to break on account of the
                // other, unreachable branch.
                return (new IfBreakDoc(b, f, ifBreak.GroupId), false);
            }

            case IndentIfBreakDoc indentIfBreak:
            {
                var (d, b) = PropagateCore(indentIfBreak.Content);
                return (new IndentIfBreakDoc(d, indentIfBreak.GroupId), b);
            }

            case LineSuffixDoc lineSuffix:
            {
                var (d, _) = PropagateCore(lineSuffix.Content);
                // A line suffix is rendered later, out of structural position; it must not force
                // the group it was *authored* in to break.
                return (new LineSuffixDoc(d), false);
            }

            case BreakParentDoc:
                return (doc, true);

            default:
                return (doc, false);
        }
    }

    private string GetIndentString(int level)
    {
        if (level <= 0)
            return string.Empty;

        if (_indentUnit.Length == 1)
            return new string(_indentUnit[0], level);

        var sb = new StringBuilder(_indentUnit.Length * level);
        for (var i = 0; i < level; i++)
            sb.Append(_indentUnit);
        return sb.ToString();
    }

    /// <summary>Removes trailing spaces/tabs from the end of the builder. Returns how many
    /// characters were removed, so callers can keep their column-position counter in sync.</summary>
    private static int TrimTrailingWhitespace(StringBuilder sb)
    {
        var end = sb.Length;
        var i = end;
        while (i > 0 && (sb[i - 1] == ' ' || sb[i - 1] == '\t'))
            i--;

        if (i == end)
            return 0;

        sb.Remove(i, end - i);
        return end - i;
    }
}
