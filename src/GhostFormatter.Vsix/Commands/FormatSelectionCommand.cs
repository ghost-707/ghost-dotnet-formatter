using GhostFormatter.Vsix.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace GhostFormatter.Vsix.Commands;

/// <summary>
/// Command: Format Selection
/// Formats the selected text in the active document.
/// </summary>
[VisualStudioContribution]
public sealed class FormatSelectionCommand : Command
{
    private readonly VsFormattingService _formattingService;

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%GhostFormatter.FormatSelection%")
    {
        Placements =
        [
            CommandPlacement.KnownPlacements.ExtensionsMenu
        ],
        Icon = new(ImageMoniker.KnownValues.FormatSelection, IconSettings.IconAndText),
        VisibleWhen = ActivationConstraint.ClientContext(
            ClientContextKey.Shell.ActiveEditorContentType,
            ".+"),
        Shortcuts = [new CommandShortcutConfiguration(ModifierKey.ControlShift, Key.G)]
    };

    /// <summary>Initializes a new instance of the <see cref="FormatSelectionCommand"/> class.</summary>
    public FormatSelectionCommand(VsFormattingService formattingService)
    {
        _formattingService = formattingService;
    }

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await _formattingService.FormatSelectionAsync(context, Extensibility, cancellationToken);
    }
}
