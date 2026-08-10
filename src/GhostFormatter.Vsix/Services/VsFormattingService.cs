using System.IO;
using GhostFormatter.Abstractions.Formatters;
using GhostFormatter.Abstractions.Model;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;
using Microsoft.VisualStudio.RpcContracts.ProgressReporting;

namespace GhostFormatter.Vsix.Services;

/// <summary>
/// Bridges the Visual Studio editor with the ghost-dotnet-formatter engine.
/// Handles document access, selection, undo transactions, and progress reporting.
/// </summary>
/// <remarks>Initializes a new instance of the <see cref="VsFormattingService"/> class.</remarks>
public sealed class VsFormattingService(
    IFormattingService formattingService,
    ILogger<VsFormattingService> logger
)
{
    /// <summary>
    /// Formats the entire active document.
    /// </summary>
    public async Task FormatActiveDocumentAsync(
        IClientContext context,
        VisualStudioExtensibility extensibility,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var editorService = extensibility.Editor();
            var activeDoc = await editorService.GetActiveTextViewAsync(context, cancellationToken);

            if (activeDoc is null)
            {
                await ShowInfoBarAsync(
                    extensibility,
                    "No active document to format.",
                    cancellationToken
                );
                return;
            }

            var document = activeDoc.Document;
            var text = document.Text.ToString();
            var filePath = document.Uri?.LocalPath;

            var request = new FormattingRequest
            {
                Text = text,
                Language = filePath is not null
                    ? formattingService.DetectLanguage(filePath)
                    : Language.Unknown,
                FilePath = filePath,
                Scope = FormattingScope.Document,
            };

            var result = await formattingService.FormatAsync(request, cancellationToken);

            if (result.Success && result.WasModified && result.FormattedText is not null)
            {
                await ApplyFormattingAsync(
                    extensibility,
                    document,
                    result.FormattedText,
                    cancellationToken
                );
                logger.LogInformation(
                    "Document formatted: {FilePath} ({Elapsed}ms)",
                    filePath,
                    result.Elapsed.TotalMilliseconds
                );
            }
            else if (!result.Success)
            {
                var message =
                    result.Diagnostics.Count > 0
                        ? result.Diagnostics[0].Message
                        : "Formatting could not be applied safely.";
                await ShowInfoBarAsync(extensibility, message, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Format document cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Format document failed");
            await ShowInfoBarAsync(
                extensibility,
                $"Formatting failed: {ex.Message}",
                cancellationToken
            );
        }
    }

    /// <summary>
    /// Formats the selected text in the active document.
    /// </summary>
    public async Task FormatSelectionAsync(
        IClientContext context,
        VisualStudioExtensibility extensibility,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var editorService = extensibility.Editor();
            var activeDoc = await editorService.GetActiveTextViewAsync(context, cancellationToken);

            if (activeDoc is null)
            {
                await ShowInfoBarAsync(extensibility, "No active document.", cancellationToken);
                return;
            }

            var selections = activeDoc.Selections;
            if (selections.Count == 0 || (selections.Count == 1 && selections[0].IsEmpty))
            {
                // No selection: format entire document instead
                await FormatActiveDocumentAsync(context, extensibility, cancellationToken);
                return;
            }

            var document = activeDoc.Document;
            var text = document.Text.ToString();
            var filePath = document.Uri?.LocalPath;
            var selection = selections[0];

            var request = new FormattingRequest
            {
                Text = text,
                Language = filePath is not null
                    ? formattingService.DetectLanguage(filePath)
                    : Language.Unknown,
                FilePath = filePath,
                Scope = FormattingScope.Selection,
                Selection = new Abstractions.Model.TextSpan(
                    selection.Start.Offset,
                    selection.End.Offset - selection.Start.Offset
                ),
            };

            var result = await formattingService.FormatAsync(request, cancellationToken);

            if (result.Success && result.WasModified && result.FormattedText is not null)
            {
                await ApplyFormattingAsync(
                    extensibility,
                    document,
                    result.FormattedText,
                    cancellationToken
                );
                logger.LogInformation("Selection formatted: {FilePath}", filePath);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Format selection cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Format selection failed");
            await ShowInfoBarAsync(
                extensibility,
                $"Formatting failed: {ex.Message}",
                cancellationToken
            );
        }
    }

    /// <summary>
    /// Formats all supported files in the current solution.
    /// </summary>
    public async Task FormatSolutionAsync(
        IClientContext context,
        VisualStudioExtensibility extensibility,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var progress = await extensibility
                .Shell()
                .StartProgressReportingAsync(
                    "Formatting Solution...",
                    new ProgressReporterOptions(isWorkCancellable: true),
                    cancellationToken
                );

            // Get solution files through the Project Query API
            var solutions = await extensibility
                .Workspaces()
                .QuerySolutionAsync(solution => solution.With(s => s.Path), cancellationToken);

            var activeSolution = solutions.FirstOrDefault();

            if (activeSolution is null || string.IsNullOrEmpty(activeSolution.Path))
            {
                await ShowInfoBarAsync(extensibility, "No solution is open.", cancellationToken);
                return;
            }

            var solutionDir = Path.GetDirectoryName(activeSolution.Path);
            if (string.IsNullOrEmpty(solutionDir))
            {
                return;
            }

            var files = await CollectFormattableFilesAsync(solutionDir, cancellationToken);
            var total = files.Count;
            var formatted = 0;
            var failed = 0;

            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = files[i];
                progress.Report(
                    new ProgressStatus(
                        (int)((i + 1) * 100 / total),
                        $"Formatting {Path.GetFileName(file)} ({i + 1}/{total})"
                    )
                );

                try
                {
                    var result = await formattingService.FormatFileAsync(file, cancellationToken);
                    if (result.Success && result.WasModified)
                    {
                        formatted++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogWarning(ex, "Failed to format {File}", file);
                }
            }

            await ShowInfoBarAsync(
                extensibility,
                $"Solution formatted: {formatted} files modified, {failed} failures, {total} total files.",
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            await ShowInfoBarAsync(
                extensibility,
                "Solution formatting cancelled.",
                cancellationToken
            );
        }
    }

    private static async Task ApplyFormattingAsync(
        VisualStudioExtensibility extensibility,
        ITextDocumentSnapshot document,
        string formattedText,
        CancellationToken cancellationToken
    )
    {
        await extensibility
            .Editor()
            .EditAsync(
                batch =>
                {
                    var editor = document.AsEditable(batch);
                    var fullRange = new TextRange(document, 0, document.Length);
                    editor.Replace(fullRange, formattedText);
                },
                cancellationToken
            );
    }

    private async Task<List<string>> CollectFormattableFilesAsync(
        string solutionDir,
        CancellationToken cancellationToken
    )
    {
        // Walk the solution directory for supported files
        var files = new List<string>();
        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".fs",
            ".fsi",
            ".fsx",
            ".cshtml",
            ".razor",
            ".html",
            ".htm",
            ".css",
            ".js",
            ".mjs",
            ".jsx",
            ".json",
            ".yml",
            ".yaml",
            ".sql",
            ".xml",
            ".config",
            ".csproj",
            ".fsproj",
            ".props",
            ".targets",
            ".md",
        };

        if (Directory.Exists(solutionDir))
        {
            foreach (
                var file in Directory.EnumerateFiles(
                    solutionDir,
                    "*.*",
                    SearchOption.AllDirectories
                )
            )
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip common non-source directories
                if (
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || file.Contains(
                        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"
                    )
                    || file.Contains(
                        $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"
                    )
                    || file.Contains(
                        $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"
                    )
                    || file.Contains(
                        $"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}"
                    )
                )
                {
                    continue;
                }

                var ext = Path.GetExtension(file);
                if (supportedExtensions.Contains(ext))
                {
                    files.Add(file);
                }
            }
        }

        return files;
    }

    private static async Task ShowInfoBarAsync(
        VisualStudioExtensibility extensibility,
        string message,
        CancellationToken cancellationToken
    )
    {
        await extensibility.Shell().ShowPromptAsync(message, PromptOptions.OK, cancellationToken);
    }
}
