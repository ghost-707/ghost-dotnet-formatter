using GhostFormatter.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace GhostFormatter.Vsix;

/// <summary>
/// The main entry point for the Visual Studio Extensibility extension.
/// </summary>
[VisualStudioContribution]
internal class GhostFormatterExtension : Extension
{
    /// <inheritdoc />
    public override ExtensionConfiguration ExtensionConfiguration =>
        new()
        {
            Metadata = new(
                id: "GhostFormatter.Vsix.C3D4E5F6-A7B8-9012-CDEF-123456789012",
                version: this.ExtensionAssemblyVersion,
                publisherName: "Ghost Software",
                displayName: "ghost-dotnet-formatter",
                description: "Intelligent multi-language code formatter for .NET solutions. Supports C#, F#, Razor, HTML, CSS, JavaScript, JSON, YAML, SQL, XML, and Markdown with strict semantic preservation."
            )
            {
                MoreInfo = "https://github.com/ghost-software/ghost-dotnet-formatter",
                Tags = ["formatter", "code formatting", "C#", "F#", "Razor", ".NET", "code style"],
                Preview = false,
            },
        };

    [VisualStudioContribution]
    internal static CommandGroupConfiguration EditorContextMenuGroup =>
        new(
            GroupPlacement.VsctParent(
                new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"),
                id: 0x040D,
                priority: 0x0600
            )
        )
        {
            Children = [GroupChild.Command<Commands.FormatDocumentCommand>()],
        };

    /// <inheritdoc />
    protected override void InitializeServices(IServiceCollection serviceCollection)
    {
        base.InitializeServices(serviceCollection);

        // Registers ILogger<T> support — required because VsFormattingService (and any
        // other service here) takes an ILogger<T> constructor parameter. Referencing the
        // Microsoft.Extensions.Logging package alone does not register it; without this
        // call, resolving/validating the container throws InvalidOperationException at
        // extension load time, before any command ever runs.
        serviceCollection.AddLogging();

        // Register all ghost-dotnet-formatter services
        serviceCollection.AddGhostFormatter();

        // Register VS-specific services
        serviceCollection.AddSingleton<Services.VsFormattingService>();
    }
}
