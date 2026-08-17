using GhostFormatter.Abstractions.Enums;

namespace GhostFormatter.Core.Services;

/// <summary>
/// Detects the programming language from file extensions and content heuristics.
/// </summary>
public sealed class LanguageDetector
{
    private static readonly Dictionary<string, Language> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = Language.CSharp,
        [".csx"] = Language.CSharp,
        [".fs"] = Language.FSharp,
        [".fsi"] = Language.FSharp,
        [".fsx"] = Language.FSharp,
        [".cshtml"] = Language.Razor,
        [".razor"] = Language.Razor,
        [".html"] = Language.Html,
        [".htm"] = Language.Html,
        [".css"] = Language.Css,
        [".js"] = Language.JavaScript,
        [".mjs"] = Language.JavaScript,
        [".jsx"] = Language.Jsx,
        [".json"] = Language.Json,
        [".jsonc"] = Language.Json,
        [".webmanifest"] = Language.Json,
        [".yml"] = Language.Yaml,
        [".yaml"] = Language.Yaml,
        [".sql"] = Language.Sql,
        [".xml"] = Language.Xml,
        [".config"] = Language.Xml,
        [".csproj"] = Language.Xml,
        [".fsproj"] = Language.Xml,
        [".vbproj"] = Language.Xml,
        [".props"] = Language.Xml,
        [".targets"] = Language.Xml,
        [".nuspec"] = Language.Xml,
        [".resx"] = Language.Xml,
        [".xaml"] = Language.Xml,
        [".md"] = Language.Markdown,
        [".markdown"] = Language.Markdown
    };

    /// <summary>
    /// Detects the language from a file path based on its extension.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>The detected language, or <see cref="Language.Unknown"/>.</returns>
    public Language DetectFromPath(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        if (string.IsNullOrEmpty(extension))
        {
            return Language.Unknown;
        }

        return ExtensionMap.TryGetValue(extension, out var language) ? language : Language.Unknown;
    }

    /// <summary>
    /// Detects the language from file content using heuristics.
    /// Used as a fallback when the extension is ambiguous or missing.
    /// </summary>
    /// <param name="content">The file content.</param>
    /// <returns>The detected language, or <see cref="Language.Unknown"/>.</returns>
    public Language DetectFromContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Language.Unknown;
        }

        var trimmed = content.TrimStart();

        // JSON
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return Language.Json;
        }

        // XML / HTML
        if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            return Language.Xml;
        }

        if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return Language.Html;
        }

        // YAML
        if (trimmed.StartsWith("---", StringComparison.Ordinal) ||
            (trimmed.Contains(':') && !trimmed.Contains('{') && !trimmed.Contains(';')))
        {
            return Language.Yaml;
        }

        // SQL
        if (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
        {
            return Language.Sql;
        }

        // C# indicators
        if (content.Contains("namespace ") || content.Contains("using System") ||
            content.Contains("public class") || content.Contains("internal class"))
        {
            return Language.CSharp;
        }

        // Razor
        if (content.Contains("@model ") || content.Contains("@page") ||
            content.Contains("@inject") || content.Contains("@code"))
        {
            return Language.Razor;
        }

        return Language.Unknown;
    }
}
