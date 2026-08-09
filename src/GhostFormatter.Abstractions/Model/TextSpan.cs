namespace GhostFormatter.Abstractions.Model;

/// <summary>
/// Represents a span of text within a document, defined by start and end positions.
/// </summary>
/// <param name="Start">The zero-based start position (inclusive).</param>
/// <param name="Length">The length of the span in characters.</param>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>Gets the zero-based end position (exclusive).</summary>
    public int End => Start + Length;

    /// <summary>Gets a value indicating whether this span is empty.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>Determines whether the span contains the specified position.</summary>
    public bool Contains(int position) => position >= Start && position < End;

    /// <summary>Determines whether the span overlaps with another span.</summary>
    public bool Overlaps(TextSpan other) =>
        Start < other.End && other.Start < End;

    /// <summary>Creates a span from start and end positions.</summary>
    public static TextSpan FromBounds(int start, int end) =>
        new(start, end - start);
}
