namespace GhostFormatter.Core.Modals;

/// <summary>Forces every group that (transitively) contains this doc to break, without itself
/// printing anything. Hardline is defined as Concat(LineDoc(Hard), BreakParentDoc) so that a
/// hard line anywhere inside a group always forces that group - and every group containing it -
/// to break.</summary>
public sealed record BreakParentDoc : Doc;
