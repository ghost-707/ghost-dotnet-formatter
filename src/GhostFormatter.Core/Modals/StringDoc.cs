namespace GhostFormatter.Core.Modals;

/// <summary>Literal text with no line breaks embedded in it. Never put '\n' inside a StringDoc -
/// use LiteralLine (via Docs.FromRawText) so the printer can track column position correctly.</summary>
public sealed record StringDoc(string Value) : Doc;
