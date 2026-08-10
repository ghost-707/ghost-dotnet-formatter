namespace GhostFormatter.Core.Modals;

/// <summary>Indents Content by one level only if the referenced group is broken. Used so that a
/// wrapped binary-expression continuation or a wrapped arrow-body only gains indentation when it
/// actually wraps.</summary>
public sealed record IndentIfBreakDoc(Doc Content, string? GroupId = null) : Doc;
