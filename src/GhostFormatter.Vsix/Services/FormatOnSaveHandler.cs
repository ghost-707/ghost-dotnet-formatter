namespace GhostFormatter.Vsix.Services;

/// <summary>
/// Placeholder / integration notes for "Format on Save".
/// </summary>
/// <remarks>
/// <para>
/// This file previously contained a second, duplicate <c>FormatDocumentCommand</c> class
/// (same name as <see cref="Commands.FormatDocumentCommand"/>, in a different namespace).
/// That was a copy/paste bug: it registered a second, redundant "Format Document" entry
/// in the Extensions menu with its own mismatched resource key, and it did not implement
/// save-triggered formatting at all. It has been removed.
/// </para>
/// <para>
/// Real "format on save" is not currently implementable purely on the out-of-process
/// VisualStudio.Extensibility SDK used by the rest of this extension: as of this writing,
/// the SDK exposes <c>ITextViewOpenClosedListener</c> and <c>ITextViewChangedListener</c>
/// editor extension points, but no document-saved event contract.
/// </para>
/// <para>
/// To implement this feature you have two supported options:
/// <list type="number">
/// <item>Add a small in-process VSSDK "bridge" package (an <c>AsyncPackage</c> that hooks
/// <c>IVsRunningDocTableEvents.OnBeforeSave</c> via the Running Document Table) that calls
/// into this extension's <c>IFormattingService</c> to format the buffer before save. See
/// Microsoft's "VSSDK-compatible VisualStudio.Extensibility extensions" documentation for
/// how in-proc and out-of-proc components are combined in a single VSIX.</item>
/// <item>Track the SDK's own roadmap for a document-saved listener and adopt it directly
/// once available, avoiding the in-proc dependency entirely.</item>
/// </list>
/// </para>
/// </remarks>
internal static class FormatOnSaveHandler
{
    // Intentionally empty: see remarks above. Wire up the in-proc bridge described there
    // before re-enabling the "Format on Save" option surfaced in FormatterSettings.
}
