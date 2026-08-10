namespace GhostFormatter.Core.Modals;

/// <summary>Buffers Content and prints it immediately before the next real newline, instead of at
/// the point it occurs in the tree. This is what lets a trailing "// comment" attach to the end
/// of the current output line even though, structurally, more Doc content for that same node
/// might still be queued after it.</summary>
public sealed record LineSuffixDoc(Doc Content) : Doc;
