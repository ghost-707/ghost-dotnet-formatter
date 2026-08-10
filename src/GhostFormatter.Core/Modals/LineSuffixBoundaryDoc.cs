namespace GhostFormatter.Core.Modals;

/// <summary>If there are any buffered line-suffixes, forces a hard line break here so they get
/// flushed before whatever comes next (used before closing a block, so a dangling trailing
/// comment can't leak past a '}').</summary>
public sealed record LineSuffixBoundaryDoc : Doc;
