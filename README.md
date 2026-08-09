# ghost-dotnet-formatter

Intelligent multi-language code formatter for .NET solutions, built as a Visual Studio 2026 extension.

Formats C#, F#, Razor, HTML, CSS, JavaScript, JSX, JSON, YAML, SQL, XML, and Markdown with **strict semantic preservation** — your code's behavior never changes, only its presentation improves.

## Features

- **Multi-language support** — Formats 12+ languages and embedded scenarios (Razor+HTML+C#, HTML+CSS+JS)
- **Semantic safety** — Never renames identifiers, reorders statements, or changes execution behavior
- **Deterministic & idempotent** — Same input always produces same output; formatting twice changes nothing
- **Roslyn-powered C#** — Uses the official compiler platform for reliable, accurate C# formatting
- **Configurable** — Fine-grained control via `.editorconfig` and `ghost-formatter.json`
- **VS integration** — Format Document, Format Selection, Format Project, Format Solution, Format on Save
- **Enterprise-ready** — Incremental formatting, async operations, cancellation, progress reporting

## Quick Start

### Build from Source

```bash
# Prerequisites: .NET 9.0 SDK, Visual Studio 2026 SDK

# Clone and build
git clone https://github.com/ghost-software/ghost-dotnet-formatter.git
cd ghost-dotnet-formatter
dotnet build

# Run tests
dotnet test

# Package the extension
dotnet build src/GhostFormatter.Vsix -c Release
```

### Install the Extension

1. Build the `.vsix` package (see above)
2. Double-click `GhostFormatter.Vsix.vsix` to install
3. Restart Visual Studio 2026
4. The formatter is ready — use Ctrl+Shift+F to format the active document

### Keyboard Shortcuts

| Command           | Shortcut        |
|-------------------|-----------------|
| Format Document   | Ctrl+Shift+F    |
| Format Selection  | Ctrl+Shift+G    |
| Format Solution   | Extensions menu |

## Configuration

### .editorconfig (recommended)

Place a `.editorconfig` at your solution root. The formatter respects standard EditorConfig properties:

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true
max_line_length = 120

[*.cs]
csharp_style_namespace_declarations = file_scoped:suggestion
dotnet_sort_system_directives_first = true

[*.{json,yml,yaml}]
indent_size = 2
```

### ghost-formatter.json (extension-specific)

For options beyond `.editorconfig`, create a `ghost-formatter.json` at your solution root:

```json
{
  "braceStyle": "allman",
  "sortUsings": true,
  "systemUsingsFirst": true,
  "namespaceStyle": "fileScoped",
  "csharp": {
    "blankLineBetweenMembers": true,
    "wrapLinqChains": true
  },
  "sql": {
    "uppercaseKeywords": true,
    "oneColumnPerLine": true
  }
}
```

See `samples/config/ghost-formatter.json` for a complete example.

## Supported Languages

| Language   | Extensions                        | Features                                        |
|------------|-----------------------------------|-------------------------------------------------|
| C#         | `.cs`, `.csx`                     | Roslyn-based, using sorting, brace formatting   |
| F#         | `.fs`, `.fsi`, `.fsx`             | Indentation-safe, trailing whitespace            |
| Razor      | `.cshtml`, `.razor`               | Mixed HTML/C#/directive formatting               |
| HTML       | `.html`, `.htm`                   | Attribute wrapping, nested indentation           |
| CSS        | `.css`                            | Property formatting, nesting support             |
| JavaScript | `.js`, `.mjs`, `.jsx`             | ASI-safe, template literal preservation          |
| JSON       | `.json`, `.jsonc`, `.webmanifest` | Value preservation, optional property sorting    |
| YAML       | `.yml`, `.yaml`                   | Anchor/alias preservation, consistent indentation|
| SQL        | `.sql`                            | Keyword casing, column alignment, semantic order |
| XML        | `.xml`, `.config`, `.csproj`, ... | Proper indentation, declaration preservation     |
| Markdown   | `.md`, `.markdown`                | Code block preservation, heading spacing         |

## Architecture

```
ghost-dotnet-formatter/
├── src/
│   ├── GhostFormatter.Abstractions   # Interfaces, models, configuration contracts
│   ├── GhostFormatter.Core           # Formatting engine, language formatters, services
│   └── GhostFormatter.Vsix           # Visual Studio 2026 extension
├── tests/
│   ├── GhostFormatter.Core.Tests          # Unit tests
│   └── GhostFormatter.Integration.Tests   # End-to-end tests
├── samples/                           # Sample configs and before/after files
└── docs/                              # Documentation
```

### Design Principles

- **Clean architecture** — Abstractions → Core → VS integration, with clear dependency flow
- **Dependency injection** — All services registered via `IServiceCollection`
- **Modular formatters** — Each language implements `ILanguageFormatter`; new languages plug in easily
- **Rule-based** — Formatting logic decomposed into composable `IFormattingRule` instances
- **Safety first** — Diagnostics reported when formatting can't safely proceed

## Extending

### Adding a New Language Formatter

1. Create a class that extends `BaseFormatter` (or implements `ILanguageFormatter`)
2. Implement `FormatCoreAsync` with your formatting logic
3. Register it in `ServiceCollectionExtensions.AddGhostFormatter()`

```csharp
public sealed class TypeScriptFormatter : BaseFormatter
{
    public override Language Language => Language.TypeScript; // Add to enum
    public override IReadOnlyList<string> SupportedExtensions => [".ts", ".tsx"];

    protected override Task<string> FormatCoreAsync(
        string text, FormattingOptions options, CancellationToken ct)
    {
        // Your formatting logic here
        return Task.FromResult(formattedText);
    }
}
```

### Adding a Custom Rule

Implement `IFormattingRule` and apply it within your formatter's pipeline.

## Testing

```bash
# All tests
dotnet test

# Unit tests only
dotnet test tests/GhostFormatter.Core.Tests

# Integration tests only
dotnet test tests/GhostFormatter.Integration.Tests

# With verbosity
dotnet test --logger "console;verbosity=detailed"
```

## License

MIT License. See [LICENSE](LICENSE) for details.
