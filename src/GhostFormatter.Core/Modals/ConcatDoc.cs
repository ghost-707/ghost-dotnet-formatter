namespace GhostFormatter.Core.Modals;

public sealed record ConcatDoc(IReadOnlyList<Doc> Parts) : Doc;
