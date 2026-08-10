namespace GhostFormatter.Core.Modals;

/// <summary>Prints BreakContents if the referenced group (by Id, or the nearest enclosing group
/// if Id is null) is in break mode, otherwise FlatContents. Used for trailing commas, optional
/// parentheses around a single lambda parameter, etc.</summary>
public sealed record IfBreakDoc(Doc BreakContents, Doc FlatContents, string? GroupId = null) : Doc;
