using GhostFormatter.Abstractions.Enums;

namespace GhostFormatter.Abstractions.Configuration;

/// <summary>
/// Root configuration for ghost-dotnet-formatter.
/// All options have sensible defaults aligned with modern .NET conventions.
/// </summary>
public sealed class FormattingOptions
{
    /// <summary>Gets or sets the number of spaces per indentation level. Default is 4.</summary>
    public int IndentSize { get; set; } = 4;

    /// <summary>Gets or sets a value indicating whether to use tabs instead of spaces.</summary>
    public bool UseTabs { get; set; } = false;

    /// <summary>Gets or sets the maximum line length before wrapping. Default is 120.</summary>
    public int MaxLineLength { get; set; } = 120;

    /// <summary>Gets or sets the brace style. Default is <see cref="BraceStyle.Allman"/>.</summary>
    public BraceStyle BraceStyle { get; set; } = BraceStyle.Allman;

    /// <summary>Gets or sets the line ending style.</summary>
    public LineEndingStyle LineEnding { get; set; } = LineEndingStyle.Lf;

    /// <summary>Gets or sets the maximum consecutive blank lines allowed. Default is 1.</summary>
    public int MaxConsecutiveBlankLines { get; set; } = 1;

    /// <summary>Gets or sets a value indicating whether to insert a final newline at end of file.</summary>
    public bool InsertFinalNewline { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to trim trailing whitespace.</summary>
    public bool TrimTrailingWhitespace { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to sort using directives.</summary>
    public bool SortUsings { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether System usings appear first.</summary>
    public bool SystemUsingsFirst { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to separate using groups.</summary>
    public bool SeparateUsingGroups { get; set; } = false;

    /// <summary>Gets or sets a value indicating whether to sort import statements (JS/TS).</summary>
    public bool SortImports { get; set; } = true;

    /// <summary>Gets or sets the namespace style for C#.</summary>
    public NamespaceStyle NamespaceStyle { get; set; } = NamespaceStyle.FileScoped;

    /// <summary>Gets or sets a value indicating whether to place a newline before opening braces.</summary>
    public bool NewlineBeforeBrace { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to place a newline after attribute lists.</summary>
    public bool NewlineAfterAttributes { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to add trailing commas in multi-line constructs.</summary>
    public TrailingCommaStyle TrailingCommas { get; set; } = TrailingCommaStyle.None;

    /// <summary>Gets or sets a value indicating whether to align similar constructs vertically.</summary>
    public bool AlignAssignments { get; set; } = false;

    /// <summary>Gets or sets a value indicating whether to insert a space after a cast.</summary>
    public bool SpaceAfterCast { get; set; } = false;

    /// <summary>Gets or sets a value indicating whether to insert spaces inside braces of single-line blocks.</summary>
    public bool SpaceWithinBraces { get; set; } = true;

    /// <summary>Gets or sets optional file header text to insert at the top of files.</summary>
    public string? FileHeader { get; set; }

    /// <summary>Gets C#-specific formatting options.</summary>
    public CSharpFormattingOptions CSharp { get; set; } = new();

    /// <summary>Gets JSON-specific formatting options.</summary>
    public JsonFormattingOptions Json { get; set; } = new();

    /// <summary>Gets SQL-specific formatting options.</summary>
    public SqlFormattingOptions Sql { get; set; } = new();

    /// <summary>Gets HTML-specific formatting options.</summary>
    public HtmlFormattingOptions Html { get; set; } = new();

    /// <summary>Creates a deep clone of this configuration.</summary>
    public FormattingOptions Clone()
    {
        return new FormattingOptions
        {
            IndentSize = IndentSize,
            UseTabs = UseTabs,
            MaxLineLength = MaxLineLength,
            BraceStyle = BraceStyle,
            LineEnding = LineEnding,
            MaxConsecutiveBlankLines = MaxConsecutiveBlankLines,
            InsertFinalNewline = InsertFinalNewline,
            TrimTrailingWhitespace = TrimTrailingWhitespace,
            SortUsings = SortUsings,
            SystemUsingsFirst = SystemUsingsFirst,
            SeparateUsingGroups = SeparateUsingGroups,
            SortImports = SortImports,
            NamespaceStyle = NamespaceStyle,
            NewlineBeforeBrace = NewlineBeforeBrace,
            NewlineAfterAttributes = NewlineAfterAttributes,
            TrailingCommas = TrailingCommas,
            AlignAssignments = AlignAssignments,
            SpaceAfterCast = SpaceAfterCast,
            SpaceWithinBraces = SpaceWithinBraces,
            FileHeader = FileHeader,
            CSharp = CSharp.Clone(),
            Json = Json.Clone(),
            Sql = Sql.Clone(),
            Html = Html.Clone(),
        };
    }
}

/// <summary>C#-specific formatting options.</summary>
public sealed class CSharpFormattingOptions
{
    /// <summary>Gets or sets a value indicating whether to use expression-bodied members where possible.</summary>
    public bool PreferExpressionBodies { get; set; } = false;

    /// <summary>Gets or sets a value indicating whether to place blank lines between members.</summary>
    public bool BlankLineBetweenMembers { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to preserve existing #region markers.</summary>
    public bool PreserveRegions { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to indent case labels in switch statements.</summary>
    public bool IndentSwitchCaseLabels { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to indent the contents of a case in a switch statement.</summary>
    public bool IndentSwitchCaseContents { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to wrap long LINQ chains.</summary>
    public bool WrapLinqChains { get; set; } = true;

    /// <summary>Gets or sets the threshold for wrapping method chains.</summary>
    public int MethodChainWrapThreshold { get; set; } = 3;

    /// <summary>Creates a deep clone.</summary>
    public CSharpFormattingOptions Clone() => (CSharpFormattingOptions)MemberwiseClone();
}

/// <summary>JSON-specific formatting options.</summary>
public sealed class JsonFormattingOptions
{
    /// <summary>Gets or sets a value indicating whether to align property values.</summary>
    public bool AlignValues { get; set; } = false;

    /// <summary>Gets or sets a value indicating whether to sort properties alphabetically.</summary>
    public bool SortProperties { get; set; } = false;

    /// <summary>Creates a deep clone.</summary>
    public JsonFormattingOptions Clone() => (JsonFormattingOptions)MemberwiseClone();
}

/// <summary>SQL-specific formatting options.</summary>
public sealed class SqlFormattingOptions
{
    /// <summary>Gets or sets a value indicating whether SQL keywords should be uppercased.</summary>
    public bool UppercaseKeywords { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether to place each column on its own line in SELECT.</summary>
    public bool OneColumnPerLine { get; set; } = true;

    /// <summary>Gets or sets the keyword to use for comma placement in column lists.</summary>
    public CommaPlacement CommaPlacement { get; set; } = CommaPlacement.Trailing;

    /// <summary>Creates a deep clone.</summary>
    public SqlFormattingOptions Clone() => (SqlFormattingOptions)MemberwiseClone();
}

/// <summary>HTML-specific formatting options.</summary>
public sealed class HtmlFormattingOptions
{
    /// <summary>Gets or sets the maximum number of attributes before wrapping.</summary>
    public int WrapAttributesThreshold { get; set; } = 3;

    /// <summary>Gets or sets a value indicating whether void elements should be self-closing.</summary>
    public bool SelfClosingVoidElements { get; set; } = false;

    /// <summary>Creates a deep clone.</summary>
    public HtmlFormattingOptions Clone() => (HtmlFormattingOptions)MemberwiseClone();
}
