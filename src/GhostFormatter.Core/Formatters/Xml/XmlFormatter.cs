using System.Text;
using System.Xml;
using System.Xml.Linq;
using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Enums;
using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Formatters.Xml;

/// <summary>
/// Formats XML documents including .csproj, .props, .targets, .config, and other XML files.
/// Preserves all element content, attributes, comments, and processing instructions.
/// </summary>
public sealed class XmlFormatter : BaseFormatter
{
    /// <inheritdoc />
    public override Language Language => Language.Xml;

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } =
    [
        ".xml",
        ".config",
        ".csproj",
        ".fsproj",
        ".vbproj",
        ".props",
        ".targets",
        ".nuspec",
        ".resx",
        ".xaml",
    ];

    /// <summary>Initializes a new instance of the <see cref="XmlFormatter"/> class.</summary>
    public XmlFormatter(ILogger<XmlFormatter> logger)
        : base(logger) { }

    /// <inheritdoc />
    public override Task<bool> CanFormatAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(false);
        }

        try
        {
            XDocument.Parse(text);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc />
    protected override Task<string> FormatCoreAsync(
        string text,
        FormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // FIX: do NOT use LoadOptions.PreserveWhitespace here. Preserving whitespace
            // keeps every original whitespace-only text node between elements as a real
            // XText node in the tree. XmlWriter's Indent=true logic only inserts its OWN
            // indentation when it judges a subtree to contain no existing text content —
            // once PreserveWhitespace has planted those old whitespace nodes, the writer
            // either (a) treats the subtree as "already has text, don't touch it" and
            // silently reproduces the original (unindented) layout, giving the appearance
            // that formatting did nothing, or (b) in more deeply nested documents, writes
            // its own new indentation ALONGSIDE the preserved old whitespace, producing
            // extra blank lines / doubled indentation that visibly grows with nesting
            // depth. Loading WITHOUT PreserveWhitespace drops insignificant
            // whitespace-only text nodes at parse time, which is exactly what lets
            // Indent=true safely take full ownership of layout.
            //
            // Trade-off to be aware of: for formats where whitespace inside element
            // content is semantically significant — e.g. .resx <value> text, or .xaml
            // <TextBlock> inner text — dropping "insignificant" whitespace on load could
            // in principle discard meaningful content if that content happened to be
            // pure whitespace. XDocument's definition of "insignificant" is standard XML
            // whitespace-in-element-content-model rules, which is the same judgment any
            // conforming XML parser makes; if a specific document type in your pipeline
            // needs whitespace preserved verbatim regardless, that format should be
            // special-cased to skip re-serialization here rather than reintroducing
            // PreserveWhitespace generally (which reintroduces the bug this fixes).
            var doc = XDocument.Parse(text);

            var hasXmlDeclaration = text.TrimStart()
                .StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = options.UseTabs ? "\t" : new string(' ', options.IndentSize),
                NewLineChars = options.LineEnding == LineEndingStyle.CrLf ? "\r\n" : "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = !hasXmlDeclaration,
                // NOTE: Setting Encoding here does not change what bytes actually get
                // produced, since we're writing into a StringBuilder/StringWriter, which
                // always reports UTF-16 for XmlWriterSettings.Encoding regardless of what
                // is configured — .NET does this because a StringWriter's backing store is
                // an in-memory UTF-16 System.String, full stop. This setting therefore has
                // NO effect on the declaration text XmlWriter emits when
                // OmitXmlDeclaration is false: it will still write whatever encoding name
                // XDocument's own declaration already specifies (or "utf-8" by XDocument's
                // own default if none was present). The real encoding of the file on disk
                // is decided by whoever writes this returned string back out — if that
                // caller writes UTF-8 bytes but the declaration inside the string claims a
                // different encoding, or vice versa, that mismatch happens at the
                // write-to-disk step, not here. This formatter only guarantees the
                // returned string's declaration text matches what the input document
                // itself declared (via doc.Declaration), which is the most this in-memory
                // string transform can honestly promise.
            };

            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, settings))
            {
                doc.WriteTo(writer);
            }

            var result = sb.ToString();

            // XDocument.WriteTo does not emit a trailing newline; add one for consistency
            // with the other formatters in this project (CssFormatter, JavaScriptFormatter,
            // CSharpFormatter) which all normalize to exactly one trailing newline.
            return Task.FromResult(result.TrimEnd('\r', '\n') + "\n");
        }
        catch (Exception ex)
        {
            // Catches both genuine parse failures (malformed XML — expected, CanFormatAsync
            // is meant to filter these out first but FormatCoreAsync can still be called
            // directly) and any unexpected XmlWriter failure. Both cases fall back to
            // returning the original text unchanged rather than throwing, so a caller
            // formatting a batch of files never has one bad file abort the whole run — but
            // this does mean a genuine formatter bug (as opposed to bad input) is silently
            // swallowed here too; the warning log is the only signal distinguishing the two
            // cases after the fact.
            Logger.LogWarning(ex, "XML formatting failed, returning original text unchanged");
            return Task.FromResult(text);
        }
    }
}
