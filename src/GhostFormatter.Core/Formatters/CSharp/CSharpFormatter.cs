using GhostFormatter.Abstractions.Model;
using GhostFormatter.Core.Printer;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using GhostFormattingOptions = GhostFormatter.Abstractions.Configuration.FormattingOptions;

namespace GhostFormatter.Core.Formatters.CSharp;

/// <summary>
/// Roslyn-based C# formatter that applies formatting rules using syntax tree rewriting.
/// Approximates CSharpier's document/printer output model by making wrapping decisions
/// based on expression complexity rather than naive length or arity heuristics, then
/// delegates final whitespace/indentation resolution to the Roslyn formatting engine.
/// </summary>
public sealed class CSharpFormatter : BaseFormatter
{
    /// <inheritdoc />
    public override Language Language => Language.CSharp;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".cs", ".csx"];

    /// <summary>Initializes a new instance of the <see cref="CSharpFormatter"/> class.</summary>
    public CSharpFormatter(ILogger<CSharpFormatter> logger)
        : base(logger) { }

    protected override async Task<string> FormatCoreAsync(
        string text,
        GhostFormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        var tree = CSharpSyntaxTree.ParseText(
            text,
            new CSharpParseOptions(LanguageVersion.Latest),
            cancellationToken: cancellationToken
        );

        var root = await tree.GetRootAsync(cancellationToken);

        // 1. Traverse the AST and map to Doc IR
        var visitor = new CSharpDocVisitor();
        var docTree = visitor.Visit(root);

        // 2. Print the Doc IR based on strict layout constraints
        var printer = new DocPrinter(printWidth: 80, indentSize: 4, useTabs: false);
        var formattedResult = printer.Print(docTree);

        return formattedResult;
    }
}
