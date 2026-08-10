namespace GhostFormatter.Abstractions.Model;

/// <summary>
/// Enumerates all languages supported by the ghost-dotnet-formatter.
/// </summary>
public enum Language
{
    /// <summary>C# source files (.cs).</summary>
    CSharp,

    /// <summary>Razor view files (.cshtml, .razor).</summary>
    Razor,

    /// <summary>HTML files (.html, .htm).</summary>
    Html,

    /// <summary>CSS files (.css).</summary>
    Css,

    /// <summary>JavaScript files (.js, .mjs).</summary>
    JavaScript,

    /// <summary>JSX files (.jsx).</summary>
    Jsx,

    /// <summary>JSON files (.json).</summary>
    Json,

    /// <summary>YAML files (.yml, .yaml).</summary>
    Yaml,

    /// <summary>SQL files (.sql).</summary>
    Sql,

    /// <summary>XML files (.xml, .config, .csproj, .props, .targets).</summary>
    Xml,

    /// <summary>Markdown files (.md).</summary>
    Markdown,

    /// <summary>Unknown or unsupported language.</summary>
    Unknown
}
