namespace GhostFormatter.Core.Modals;

/// <summary>
/// A breakable unit. The printer first tries to print Content in "flat" mode (all Line docs
/// collapse to spaces/nothing); if it doesn't fit in the remaining width - or ShouldBreak is
/// set, or Content contains a hard break anywhere (see PropagateBreaks) - Content is printed
/// in "break" mode instead (all Line docs become real newlines).
///
/// Groups nest independently: a broken outer group does NOT force an inner group to break
/// unless the inner group's own content doesn't fit or itself contains a hard break. This is
/// the single most important correctness property for "Group A / Group B / Group C" nesting
/// requested in the brief, and it is exactly the property the original DocPrinter got wrong
/// (it OR'd the parent's "broken" flag into every descendant unconditionally).
/// </summary>
public sealed record GroupDoc(Doc Content, bool ShouldBreak = false, string? Id = null) : Doc;
