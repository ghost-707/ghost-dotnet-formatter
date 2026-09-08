using GhostFormatter.Abstractions.Enums;
using GhostFormatter.Core.Formatters.Css;
using GhostFormatter.Core.Formatters.JavaScript;
using GhostFormatter.Core.Printer;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.Extensions.Logging;
using GhostFormattingOptions = GhostFormatter.Abstractions.Configuration.FormattingOptions;

namespace GhostFormatter.Core.Formatters.Razor;

/// <summary>
/// Formats .cshtml/.razor files by parsing with the real Razor language engine
/// (Microsoft.AspNetCore.Razor.Language — the same frontend ASP.NET Core itself uses to
/// compile these files) and walking the resulting syntax tree, instead of scanning lines
/// with regex/character heuristics. This replaces the previous line-oriented RazorFormatter,
/// which tracked HTML nesting depth and C# brace depth with two independently-incremented
/// counters. That approach could not stay correct in general: Razor's grammar is
/// context-sensitive (markup can open inside C#, which can open inside markup, arbitrarily),
/// and any construct that didn't fit the "classify this physical line by its first
/// character" assumption (see class remarks in the old formatter — a closing tag trailing
/// plain text with no leading '&lt;', e.g. "on Ventoro&lt;/a&gt;") silently desynced the
/// counters and corrupted every line after it for the rest of the file.
///
/// Depth here is structural — it comes from recursion over the parsed tree — so a
/// closing tag is always matched to the opener the parser already matched it to, never to
/// a heuristic guess re-derived per physical line.
/// </summary>
public sealed class RazorFormatter : BaseFormatter
{
    private readonly CssFormatter _cssFormatter;
    private readonly JavaScriptFormatter _javaScriptFormatter;

    public override Language Language => Language.Razor;

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".cshtml", ".razor"];

    public RazorFormatter(
        ILogger<RazorFormatter> logger,
        CssFormatter cssFormatter,
        JavaScriptFormatter javaScriptFormatter
    )
        : base(logger)
    {
        _cssFormatter = cssFormatter;
        _javaScriptFormatter = javaScriptFormatter;
    }

    protected override async Task<string> FormatCoreAsync(
        string text,
        GhostFormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Parse with the real Razor engine. RazorProjectEngine needs a filesystem/root even
        // though we only ever hand it one in-memory document — a throwaway empty filesystem
        // is the normal pattern for "format this one buffer" usage.
        var fileSystem = RazorProjectFileSystem.Create(Directory.GetCurrentDirectory());
        var projectEngine = RazorProjectEngine.Create(
            RazorConfiguration.Default,
            fileSystem,
            _ => { }
        );

        var sourceDocument = RazorSourceDocument.Create(text, "Formatting.cshtml");
        var codeDocument = projectEngine.Process(
            sourceDocument,
            fileKind: null,
            importSources: [],
            tagHelpers: []
        );
        var syntaxTree = codeDocument.GetSyntaxTree();

        // RazorSyntaxTree.Root's declared return type is the internal SyntaxNode class, so
        // it must stay as plain `object` — never pattern-matched or cast to SyntaxNode.
        var root = RazorReflectionAccess.GetProperty(syntaxTree, "Root");
        if (root is null)
        {
            Logger.LogWarning(
                "RazorFormatter: could not access RazorSyntaxTree.Root via reflection " +
                "(property not found) — returning original text unformatted."
            );
            return text;
        }

        foreach (var diagnostic in syntaxTree.Diagnostics)
        {
            Logger.LogDebug(
                "RazorFormatter: parse diagnostic {Id} at {Span}: {Message}",
                diagnostic.Id, diagnostic.Span, diagnostic.GetMessage()
            );
        }

        var visitor = new RazorDocVisitor(options, _cssFormatter, _javaScriptFormatter, Logger);
        var doc = visitor.VisitRoot(root);

        var printerOptions = new DocPrinterOptions(
            PrintWidth: options.MaxLineLength,
            IndentSize: options.IndentSize,
            UseTabs: options.UseTabs
        );

        var printer = new DocPrinter(printerOptions);
        var formatted = printer.Print(doc);

        return formatted.TrimEnd('\n', '\r') + "\n";
    }
}
