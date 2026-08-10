namespace GhostFormatter.Core.Modals;

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
