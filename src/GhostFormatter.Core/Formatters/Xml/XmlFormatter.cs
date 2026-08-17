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
        [".xml", ".config", ".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".nuspec", ".resx", ".xaml"];

    /// <summary>Initializes a new instance of the <see cref="XmlFormatter"/> class.</summary>
    public XmlFormatter(ILogger<XmlFormatter> logger) : base(logger) { }

    /// <inheritdoc />
    public override Task<bool> CanFormatAsync(string text, CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = options.UseTabs ? "\t" : new string(' ', options.IndentSize),
                NewLineChars = options.LineEnding == LineEndingStyle.CrLf ? "\r\n" : "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = !text.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase),
                Encoding = new UTF8Encoding(false)
            };

            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, settings))
            {
                doc.WriteTo(writer);
            }

            return Task.FromResult(sb.ToString());
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "XML parsing failed, returning original text");
            return Task.FromResult(text);
        }
    }
}
