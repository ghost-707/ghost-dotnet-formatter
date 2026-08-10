using GhostFormatter.Abstractions.Formatters;
using GhostFormatter.Abstractions.Model;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Editor;
using Microsoft.VisualStudio.Extensibility.Shell;

namespace GhostFormatter.Vsix.Commands;

/// <summary>
/// Command: Format Document
/// Formats the entire active document using ghost-dotnet-formatter.
/// </summary>
[VisualStudioContribution]
public sealed class FormatDocumentCommand : Command
{
    private readonly IFormattingService _formattingService;

    /// <summary>Initializes a new instance of the <see cref="FormatDocumentCommand"/> class.</summary>
    public FormatDocumentCommand(IFormattingService formattingService)
    {
        _formattingService = formattingService;
    }

    /// <inheritdoc />
    public override CommandConfiguration CommandConfiguration =>
        new("%GhostFormatter.FormatDocument%")
        {
            Placements = [CommandPlacement.KnownPlacements.ExtensionsMenu],
            Icon = new(ImageMoniker.KnownValues.FormatDocument, IconSettings.IconAndText),
            VisibleWhen = ActivationConstraint.ClientContext(
                ClientContextKey.Shell.ActiveEditorContentType,
                ".+"
            ),
            Shortcuts = [new CommandShortcutConfiguration(ModifierKey.LeftAlt, Key.F)],
        };

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(
        IClientContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // 1. Get the active document
            var activeTextView = await context.GetActiveTextViewAsync(cancellationToken);
            if (activeTextView is null)
            {
                await this
                    .Extensibility.Shell()
                    .ShowPromptAsync(
                        "ghost-dotnet-formatter: no active text view found.",
                        PromptOptions.OK,
                        cancellationToken
                    );
                return;
            }

            var document = activeTextView.Document;
            // document.Text is a TextRange (a lazy reference into the document), not a
            // string — .ToString() on it returns the type name, not the content.
            // CopyToString() is the documented way to materialize the actual text.
            var text = document.Text.CopyToString();
            var filePath = document.Uri?.LocalPath;

            if (string.IsNullOrEmpty(filePath))
            {
                await this
                    .Extensibility.Shell()
                    .ShowPromptAsync(
                        "ghost-dotnet-formatter: active document has no file path.",
                        PromptOptions.OK,
                        cancellationToken
                    );
                return;
            }

            // 2. Format the text
            var language = _formattingService.DetectLanguage(filePath);
            if (language == Language.Unknown)
            {
                await this
                    .Extensibility.Shell()
                    .ShowPromptAsync(
                        $"ghost-dotnet-formatter: could not detect a supported language for '{filePath}'.",
                        PromptOptions.OK,
                        cancellationToken
                    );
                return;
            }

            var request = new FormattingRequest
            {
                Text = text ?? "",
                Language = language,
                FilePath = filePath,
                Scope = FormattingScope.Document,
            };

            var result = await _formattingService.FormatAsync(request, cancellationToken);

            if (!result.Success)
            {
                var messages = string.Join(
                    "; ",
                    result.Diagnostics.Select(d =>
                        d.Line is not null
                            ? $"{d.Message} (line {d.Line}, col {d.Column})"
                            : d.Message
                    )
                );
                await this
                    .Extensibility.Shell()
                    .ShowPromptAsync(
                        $"ghost-dotnet-formatter: formatting failed for {language}. {messages}",
                        PromptOptions.OK,
                        cancellationToken
                    );
                return;
            }

            if (!result.WasModified)
            {
                await this
                    .Extensibility.Shell()
                    .ShowPromptAsync(
                        $"ghost-dotnet-formatter: {language} formatter ran successfully — no changes were needed.",
                        PromptOptions.OK,
                        cancellationToken
                    );
                return;
            }

            // 3. Apply the edits using Extensibility APIs.
            // Re-fetch a fresh text view/document right before editing, rather than
            // reusing the one captured at the start of the method — formatting can take
            // real time, and ITextDocumentSnapshot is immutable, so the earlier snapshot
            // may no longer match the live buffer by the time we get here.
            string formattedText = result.FormattedText!;

            var freshTextView = await context.GetActiveTextViewAsync(cancellationToken);
            if (freshTextView is null)
            {
                await this
                    .Extensibility.Shell()
                    .ShowPromptAsync(
                        "ghost-dotnet-formatter: the document closed before formatting could be applied.",
                        PromptOptions.OK,
                        cancellationToken
                    );
                return;
            }

            var freshDocument = freshTextView.Document;

            var editResult = await this
                .Extensibility.Editor()
                .EditAsync(
                    batch =>
                    {
                        var editor = freshDocument.AsEditable(batch);
                        var fullRange = new TextRange(freshDocument, 0, freshDocument.Length);
                        editor.Replace(fullRange, formattedText);
                    },
                    cancellationToken
                );

            if (!editResult.Succeeded)
            {
                await this
                    .Extensibility.Shell()
                    .ShowPromptAsync(
                        "ghost-dotnet-formatter: the edit was rejected because the document changed while formatting was in progress. Nothing was modified — please try again.",
                        PromptOptions.OK,
                        cancellationToken
                    );
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            await this
                .Extensibility.Shell()
                .ShowPromptAsync(
                    $"ghost-dotnet-formatter: unexpected error — {ex.GetType().Name}: {ex.Message}",
                    PromptOptions.OK,
                    cancellationToken
                );
        }
    }
}
