using GhostFormatter.Vsix.Services;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace GhostFormatter.Vsix.Commands;

/// <summary>
/// Command: Format Solution
/// Formats all supported files in the open solution.
/// Reports progress and supports cancellation.
/// </summary>
[VisualStudioContribution]
public sealed class FormatSolutionCommand : Command
{
    private readonly VsFormattingService _formattingService;

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration => new("%GhostFormatter.FormatSolution%")
    {
        Placements =
        [
            CommandPlacement.KnownPlacements.ExtensionsMenu
        ],
        Icon = new(ImageMoniker.KnownValues.FormatDocument, IconSettings.IconAndText)
    };

    /// <summary>Initializes a new instance of the <see cref="FormatSolutionCommand"/> class.</summary>
    public FormatSolutionCommand(VsFormattingService formattingService)
    {
        _formattingService = formattingService;
    }

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await _formattingService.FormatSolutionAsync(context, Extensibility, cancellationToken);
    }
}
