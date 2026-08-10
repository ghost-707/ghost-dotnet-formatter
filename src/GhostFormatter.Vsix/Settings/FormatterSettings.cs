using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace GhostFormatter.Vsix.Settings;

/// <summary>
/// User-configurable formatting settings, contributed via the VisualStudio.Extensibility
/// Settings API (available since Extensibility SDK 17.11).
/// </summary>
/// <remarks>
/// The Settings API is still marked preview by Microsoft (hence the pragma below). Where
/// exactly these show up in the VS UI (a classic Tools &gt; Options node vs. the newer
/// unified settings surface) is controlled by Visual Studio itself and can change between
/// VS releases — verify the actual placement in both VS 2022 and VS 2026 once built, since
/// this sandbox cannot run Visual Studio to confirm it directly.
///
/// This class only *declares* the settings so they're visible/editable. Nothing in
/// <see cref="Services.VsFormattingService"/> reads them yet — wire that up as the next
/// step so changing a value actually changes formatting behavior.
/// </remarks>
#pragma warning disable VSEXTPREVIEW_SETTINGS
internal static class FormatterSettings
{
    [VisualStudioContribution]
    internal static SettingCategory Category { get; } =
        new("ghostFormatter", "%GhostFormatter.Settings.Category.DisplayName%")
        {
            GenerateObserverClass = true,
        };

    [VisualStudioContribution]
    internal static Setting.Integer IndentSize { get; } = new(
        "indentSize",
        "%GhostFormatter.Settings.IndentSize.DisplayName%",
        Category,
        defaultValue: 4)
    {
        Description = "%GhostFormatter.Settings.IndentSize.Description%",
    };

    [VisualStudioContribution]
    internal static Setting.Boolean UseTabs { get; } = new(
        "useTabs",
        "%GhostFormatter.Settings.UseTabs.DisplayName%",
        Category,
        defaultValue: false)
    {
        Description = "%GhostFormatter.Settings.UseTabs.Description%",
    };

    [VisualStudioContribution]
    internal static Setting.Integer MaxLineLength { get; } = new(
        "maxLineLength",
        "%GhostFormatter.Settings.MaxLineLength.DisplayName%",
        Category,
        defaultValue: 120)
    {
        Description = "%GhostFormatter.Settings.MaxLineLength.Description%",
    };

    [VisualStudioContribution]
    internal static Setting.Boolean FormatOnSave { get; } = new(
        "formatOnSave",
        "%GhostFormatter.Settings.FormatOnSave.DisplayName%",
        Category,
        defaultValue: false)
    {
        Description = "%GhostFormatter.Settings.FormatOnSave.Description%",
    };
}
#pragma warning restore VSEXTPREVIEW_SETTINGS
