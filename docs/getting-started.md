# Getting Started with ghost-dotnet-formatter

## Prerequisites

- Visual Studio 2026 (Community, Professional, or Enterprise)
- .NET 9.0 SDK (LTS)
- Windows 10/11

## Installation

### From VSIX Package

1. Download the latest `GhostFormatter.Vsix.vsix` from the Releases page
2. Close all Visual Studio instances
3. Double-click the `.vsix` file
4. Follow the installer prompts
5. Launch Visual Studio 2026

### From Source

```bash
git clone https://github.com/ghost-software/ghost-dotnet-formatter.git
cd ghost-dotnet-formatter
dotnet build src/GhostFormatter.Vsix -c Release
```

Then install the `.vsix` from the build output.

## Usage

### Format a Document

- **Keyboard:** Press `Ctrl+Shift+F` with a document open
- **Menu:** Extensions → ghost-dotnet-formatter → Format Document
- **Context menu:** Right-click in the editor → Format Document (Ghost)

### Format a Selection

- Select text in the editor
- Press `Ctrl+Shift+G`
- Only the selected region is formatted

### Format on Save

Format-on-save is enabled by default. To disable:

1. Go to Tools → Options
2. Navigate to ghost-dotnet-formatter → General
3. Uncheck "Format on Save"

### Format an Entire Solution

- Menu: Extensions → ghost-dotnet-formatter → Format Solution
- Shows progress bar and supports cancellation
- Skips `bin/`, `obj/`, `node_modules/`, `.git/`, `.vs/`

## Configuration

ghost-dotnet-formatter reads settings from two sources:

### 1. .editorconfig (Standard)

Supported properties:

| Property                     | Values                    | Default  |
|-----------------------------|---------------------------|----------|
| `indent_style`               | `space`, `tab`           | `space`  |
| `indent_size`                | integer                  | `4`      |
| `end_of_line`                | `lf`, `crlf`             | `lf`     |
| `insert_final_newline`       | `true`, `false`          | `true`   |
| `trim_trailing_whitespace`   | `true`, `false`          | `true`   |
| `max_line_length`            | integer or `off`         | `120`    |

### 2. ghost-formatter.json (Extension-specific)

Place at your solution root for advanced options:

```json
{
  "braceStyle": "allman",
  "sortUsings": true,
  "systemUsingsFirst": true,
  "namespaceStyle": "fileScoped",
  "maxConsecutiveBlankLines": 1
}
```

See `samples/config/ghost-formatter.json` for all options.

## Supported File Types

The formatter detects file types automatically by extension:

| Category  | Extensions                                          |
|-----------|-----------------------------------------------------|
| C#        | `.cs`, `.csx`                                      |
| F#        | `.fs`, `.fsi`, `.fsx`                              |
| Razor     | `.cshtml`, `.razor`                                |
| HTML      | `.html`, `.htm`                                    |
| CSS       | `.css`                                             |
| JavaScript| `.js`, `.mjs`, `.jsx`                              |
| JSON      | `.json`, `.jsonc`, `.webmanifest`                  |
| YAML      | `.yml`, `.yaml`                                    |
| SQL       | `.sql`                                             |
| XML       | `.xml`, `.config`, `.csproj`, `.props`, `.targets` |
| Markdown  | `.md`, `.markdown`                                 |

## Troubleshooting

### Formatting doesn't apply

- Check the Output window (View → Output, select "ghost-dotnet-formatter")
- Ensure the file type is supported (see table above)
- Look for syntax errors that prevent safe formatting

### Formatting changes code behavior

This should never happen. If it does:

1. Undo immediately (Ctrl+Z)
2. File a bug report with the original code
3. Formatting that changes behavior is a critical bug

### Performance issues on large solutions

- Use Format Document (single file) instead of Format Solution
- The formatter uses incremental processing and async operations
- Check if `.editorconfig` resolution is slow (deeply nested directories)

## Reporting Issues

When reporting a bug, please include:

1. The file content (or a minimal reproduction)
2. Your `.editorconfig` and/or `ghost-formatter.json`
3. The expected vs. actual output
4. Visual Studio version and extension version
5. Any diagnostics from the Output window
