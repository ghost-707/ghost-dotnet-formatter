using System.Text;
using GhostFormatter.Core.Docs;

namespace GhostFormatter.Core.Printer;

public class DocPrinter
{
    private readonly int _printWidth;
    private readonly string _indentString;

    public DocPrinter(int printWidth = 80, int indentSize = 4, bool useTabs = false)
    {
        _printWidth = printWidth;
        _indentString = useTabs ? "\t" : new string(' ', indentSize);
    }

    public string Print(Doc root)
    {
        var sb = new StringBuilder();
        // The command stack tracks what we need to print, the current indent, and whether we are in a "broken" group
        var commands = new Stack<(int Indent, bool Broken, Doc Doc)>();
        commands.Push((0, false, root));

        int position = 0;

        while (commands.Count > 0)
        {
            var (indent, broken, doc) = commands.Pop();

            switch (doc)
            {
                case StringDoc(var val):
                    sb.Append(val);
                    position += val.Length;
                    break;

                case ConcatDoc(var parts):
                    // Push in reverse order so they pop in the correct sequence
                    for (int i = parts.Count - 1; i >= 0; i--)
                    {
                        commands.Push((indent, broken, parts[i]));
                    }
                    break;

                case IndentDoc(var content):
                    commands.Push((indent + 1, broken, content));
                    break;

                case LineDoc(var isHard, var isSoft):
                    if (isHard || broken)
                    {
                        sb.AppendLine();
                        var currentIndent = GetIndentString(indent);
                        sb.Append(currentIndent);
                        position = currentIndent.Length;
                    }
                    else
                    {
                        if (!isSoft)
                        {
                            sb.Append(' ');
                            position++;
                        }
                    }
                    break;

                case GroupDoc(var content):
                    // Check if the group fits on the current line
                    bool groupBreaks = broken || !Fits(content, commands, _printWidth - position);
                    commands.Push((indent, groupBreaks, content));
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Look-ahead function to determine if a Doc can fit on a single line within the remaining width.
    /// </summary>
    private bool Fits(Doc doc, Stack<(int, bool, Doc)> currentCommands, int remainingWidth)
    {
        var queue = new Queue<Doc>();
        queue.Enqueue(doc);
        int width = remainingWidth;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            switch (current)
            {
                case StringDoc(var val):
                    width -= val.Length;
                    if (width < 0)
                        return false;
                    break;
                case ConcatDoc(var parts):
                    foreach (var p in parts)
                        queue.Enqueue(p);
                    break;
                case IndentDoc(var content):
                    queue.Enqueue(content);
                    break;
                case GroupDoc(var content):
                    queue.Enqueue(content);
                    break;
                case LineDoc(var isHard, var isSoft):
                    if (isHard)
                        return false; // Hardlines immediately break the group
                    if (!isSoft)
                    {
                        width -= 1; // standard space
                        if (width < 0)
                            return false;
                    }
                    break;
            }
        }

        return true;
    }

    private string GetIndentString(int indentLevel)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < indentLevel; i++)
            sb.Append(_indentString);
        return sb.ToString();
    }
}
