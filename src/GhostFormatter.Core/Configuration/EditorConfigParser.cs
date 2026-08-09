using Microsoft.Extensions.Logging;

namespace GhostFormatter.Core.Configuration;

/// <summary>
/// Parses .editorconfig files and returns applicable settings for a given file path.
/// Implements a simplified .editorconfig parser that handles common properties.
/// </summary>
internal sealed class EditorConfigParser
{
    private readonly ILogger _logger;

    public EditorConfigParser(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses .editorconfig files up the directory tree and returns merged settings.
    /// </summary>
    public Task<EditorConfigSettings> ParseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var settings = new EditorConfigSettings();
        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        var extension = Path.GetExtension(filePath);

        if (directory is null)
        {
            return Task.FromResult(settings);
        }

        // Walk up the directory tree collecting .editorconfig files
        var configFiles = new List<string>();
        var current = directory;
        var isRoot = false;

        while (current is not null && !isRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configPath = Path.Combine(current, ".editorconfig");
            if (File.Exists(configPath))
            {
                configFiles.Add(configPath);

                // Check if this is a root config
                var content = File.ReadAllText(configPath);
                if (content.Contains("root = true", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("root=true", StringComparison.OrdinalIgnoreCase))
                {
                    isRoot = true;
                }
            }

            current = Path.GetDirectoryName(current);
        }

        // Apply configs from root to leaf (closest to file wins)
        configFiles.Reverse();

        foreach (var configPath in configFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sections = ParseConfigFile(configPath);
                ApplyMatchingSections(settings, sections, filePath, extension);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing .editorconfig at {Path}", configPath);
            }
        }

        return Task.FromResult(settings);
    }

    private static List<EditorConfigSection> ParseConfigFile(string path)
    {
        var sections = new List<EditorConfigSection>();
        EditorConfigSection? currentSection = null;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();

            // Skip comments and empty lines
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            // Section header
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = new EditorConfigSection
                {
                    Glob = line[1..^1].Trim()
                };
                sections.Add(currentSection);
                continue;
            }

            // Key-value pair
            var eqIndex = line.IndexOf('=');
            if (eqIndex > 0 && currentSection is not null)
            {
                var key = line[..eqIndex].Trim().ToLowerInvariant();
                var value = line[(eqIndex + 1)..].Trim().ToLowerInvariant();
                currentSection.Properties[key] = value;
            }
        }

        return sections;
    }

    private static void ApplyMatchingSections(
        EditorConfigSettings settings,
        List<EditorConfigSection> sections,
        string filePath,
        string? extension)
    {
        foreach (var section in sections)
        {
            if (!GlobMatches(section.Glob, filePath, extension))
            {
                continue;
            }

            foreach (var (key, value) in section.Properties)
            {
                ApplyProperty(settings, key, value);
            }
        }
    }

    private static bool GlobMatches(string glob, string filePath, string? extension)
    {
        // Handle common glob patterns
        if (glob == "*")
        {
            return true;
        }

        // Extension-based globs like *.cs or *.{cs,fs}
        if (glob.StartsWith("*.") && extension is not null)
        {
            var globExt = glob[1..]; // e.g., ".cs" or ".{cs,fs}"

            if (globExt.Contains('{') && globExt.Contains('}'))
            {
                // Handle brace expansion: *.{cs,fs,razor}
                var inner = globExt[(globExt.IndexOf('{') + 1)..globExt.IndexOf('}')];
                var extensions = inner.Split(',').Select(e => "." + e.Trim());
                return extensions.Any(e => string.Equals(extension, e, StringComparison.OrdinalIgnoreCase));
            }

            return string.Equals(extension, globExt, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static void ApplyProperty(EditorConfigSettings settings, string key, string value)
    {
        switch (key)
        {
            case "indent_size":
                if (int.TryParse(value, out var indentSize))
                {
                    settings.IndentSize = indentSize;
                }
                break;

            case "indent_style":
                settings.IndentStyle = value switch
                {
                    "tab" => IndentStyleValue.Tab,
                    "space" => IndentStyleValue.Space,
                    _ => null
                };
                break;

            case "end_of_line":
                settings.EndOfLine = value switch
                {
                    "lf" => EndOfLineValue.Lf,
                    "crlf" => EndOfLineValue.CrLf,
                    "cr" => EndOfLineValue.Cr,
                    _ => null
                };
                break;

            case "insert_final_newline":
                settings.InsertFinalNewline = value == "true";
                break;

            case "trim_trailing_whitespace":
                settings.TrimTrailingWhitespace = value == "true";
                break;

            case "max_line_length":
                if (value != "off" && int.TryParse(value, out var maxLen))
                {
                    settings.MaxLineLength = maxLen;
                }
                break;
        }
    }
}

internal sealed class EditorConfigSection
{
    public string Glob { get; set; } = "*";
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Parsed .editorconfig settings applicable to a specific file.
/// </summary>
internal sealed class EditorConfigSettings
{
    public int? IndentSize { get; set; }
    public IndentStyleValue? IndentStyle { get; set; }
    public EndOfLineValue? EndOfLine { get; set; }
    public bool? InsertFinalNewline { get; set; }
    public bool? TrimTrailingWhitespace { get; set; }
    public int? MaxLineLength { get; set; }
}

internal enum IndentStyleValue { Space, Tab }
internal enum EndOfLineValue { Lf, CrLf, Cr }
