namespace GhostFormatter.Core.Modals;

/// <summary>Removes trailing spaces/tabs already written on the current output line. Used rarely,
/// mainly to keep otherwise-empty lines clean.</summary>
public sealed record TrimDoc : Doc;
