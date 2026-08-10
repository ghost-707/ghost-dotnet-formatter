using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GhostFormatter.Abstractions.Model;
using GhostFormatter.Core.Docs;
using GhostFormatter.Core.Printer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using DiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;
using GhostFormattingOptions = GhostFormatter.Abstractions.Configuration.FormattingOptions;

namespace GhostFormatter.Core.Formatters.CSharp;

public sealed class CSharpFormatter : BaseFormatter
{
    public override Language Language => Language.CSharp;

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".cs", ".csx"];

    public CSharpFormatter(ILogger<CSharpFormatter> logger)
        : base(logger) { }

    protected override async Task<string> FormatCoreAsync(
        string text,
        GhostFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(
            text,
            new CSharpParseOptions(LanguageVersion.Latest),
            cancellationToken: cancellationToken
        );

        var root = await tree.GetRootAsync(cancellationToken);

        foreach (var diagnostic in tree.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                // We still attempt to format invalid/incomplete code (format-on-type / format-on
                // -save both routinely hand us a transiently-invalid buffer), but we log so callers
                // can distinguish "formatter bug" output from "your source didn't parse" output.
                Logger.LogDebug(
                    "CSharpFormatter: source has a parse error at {Location}: {Message}",
                    diagnostic.Location.GetLineSpan(),
                    diagnostic.GetMessage()
                );
            }
        }

        // NOTE ON GhostFormattingOptions: the original code ignored `options` entirely and
        // hard-coded 80 / 4 / false. GhostFormatter.Abstractions.Configuration.FormattingOptions
        // was not part of the supplied source, so its real property names are unknown. Rather than
        // guess a name and risk a compile error either way, every value below is read reflectively
        // with a CSharpier-matching default (printWidth 100, indentSize 4, spaces) and falls back
        // silently if the property doesn't exist. Once you can see the real FormattingOptions
        // type, replace TryGetOption(...) with direct property access - the rest of the pipeline
        // (DocPrinterOptions, DocPrinter, the visitor) is unaffected either way.
        var printerOptions = new DocPrinterOptions(
            PrintWidth: TryGetOption(options, "PrintWidth", 100),
            IndentSize: TryGetOption(options, "IndentSize", 4),
            UseTabs: TryGetOption(options, "UseTabs", false)
        );

        var visitorOptions = new CSharpDocVisitorOptions(
            ThrowOnUnsupportedSyntax: TryGetOption(options, "ThrowOnUnsupportedSyntax", DefaultThrowOnUnsupported)
        );

        var visitor = new CSharpDocVisitor(visitorOptions);

        Doc docTree;
        try
        {
            docTree = visitor.Visit(root) ?? (Doc)"";
        }
        catch (UnsupportedSyntaxException ex)
        {
            Logger.LogWarning(
                ex,
                "CSharpFormatter: encountered unsupported syntax ({Kind}) at {Location}; " +
                    "falling back is disabled for this run so the coverage gap is visible instead " +
                    "of silently emitting raw, unformatted text.",
                ex.SyntaxKind,
                ex.Location
            );
            throw;
        }

        var printer = new DocPrinter(printerOptions);
        var formatted = printer.Print(docTree);

        // Normalize to exactly one trailing newline, matching CSharpier's own file-level output.
        formatted = formatted.TrimEnd('\n', '\r') + "\n";

        return formatted;
    }

    /// <summary>
    /// Default for whether unsupported syntax throws (development/CI posture, recommended) or
    /// falls back to a clearly-marked raw block (see CSharpDocVisitor.DefaultVisit). Wire this to
    /// GhostFormattingOptions once you've decided how CI should treat coverage gaps; see point 18
    /// of the brief - a raw fallback is only acceptable when it's impossible to mistake for real
    /// formatting.
    /// </summary>
    private const bool DefaultThrowOnUnsupported = true;

    private static T TryGetOption<T>(GhostFormattingOptions options, string propertyName, T fallback)
    {
        var prop = typeof(GhostFormattingOptions).GetProperty(propertyName);
        if (prop == null || !typeof(T).IsAssignableFrom(prop.PropertyType))
            return fallback;

        return prop.GetValue(options) is T typed ? typed : fallback;
    }
}
