namespace GhostFormatter.Core.Docs;

public abstract record Doc
{
    public static implicit operator Doc(string value) => new StringDoc(value);
}

/// <summary>Literal string text (e.g., "public", "{", "myVar").</summary>
public record StringDoc(string Value) : Doc;

/// <summary>Combines multiple docs together sequentially.</summary>
public record ConcatDoc(IReadOnlyList<Doc> Parts) : Doc;

/// <summary>
/// A line break. If the enclosing Group fits on one line, this acts as a Space (or empty string).
/// If the group breaks, it outputs a newline + current indentation.
/// </summary>
public record LineDoc(bool IsHard = false, bool IsSoft = false) : Doc;

/// <summary>Increases the indentation level for all nested Docs.</summary>
public record IndentDoc(Doc Content) : Doc;

/// <summary>
/// A group of docs. The printer tries to fit this group onto a single line.
/// If it exceeds the print width, the group "breaks", forcing all LineDocs inside it to become newlines.
/// </summary>
public record GroupDoc(Doc Content) : Doc;

// Factory helpers for cleaner syntax in the visitor
public static class Docs
{
    public static Doc Concat(params Doc[] docs) => new ConcatDoc(docs);

    public static Doc Group(params Doc[] docs) => new GroupDoc(new ConcatDoc(docs));

    public static Doc Indent(params Doc[] docs) => new IndentDoc(new ConcatDoc(docs));

    public static Doc Line => new LineDoc(); // Space if flat, newline if broken
    public static Doc SoftLine => new LineDoc(IsSoft: true); // Empty if flat, newline if broken
    public static Doc HardLine => new LineDoc(IsHard: true); // Always a newline

    public static Doc Join(Doc separator, IEnumerable<Doc> docs)
    {
        var list = docs.ToList();
        if (list.Count == 0)
            return new StringDoc("");

        var result = new List<Doc>();
        for (int i = 0; i < list.Count; i++)
        {
            result.Add(list[i]);
            if (i < list.Count - 1)
                result.Add(separator);
        }
        return new ConcatDoc(result);
    }
}
