# Developer Guide — Extending ghost-dotnet-formatter

## Overview

ghost-dotnet-formatter is designed for extensibility. The architecture separates
concerns cleanly so you can add new languages, new rules, or new integrations
without touching existing code.

## Project Structure

### GhostFormatter.Abstractions

Contains all interfaces and models. This assembly has **zero implementation**
and depends only on `Microsoft.Extensions.Logging.Abstractions`.

Key types:

- `ILanguageFormatter` — The contract every language formatter implements.
- `IFormatterRegistry` — Resolves formatters by language or file extension.
- `IFormattingService` — The top-level orchestrator.
- `IOptionsProvider` — Resolves formatting options for a file.
- `IFormattingRule` — A single composable formatting transformation.
- `FormattingRequest` — Input to a formatting operation.
- `FormattingResult` — Output including formatted text, diagnostics, and timing.
- `FormattingOptions` — The full set of configurable formatting preferences.

### GhostFormatter.Core

Contains all formatting logic and services. Depends on Abstractions.

Key components:

- `FormatterRegistry` — Auto-discovers formatters via DI.
- `FormattingService` — Coordinates detection, options, and dispatch.
- `OptionsProvider` — Merges defaults with `.editorconfig`.
- `BaseFormatter` — Abstract base with common logic (timing, validation, normalization).
- Language formatters in `Formatters/{Language}/`.

### GhostFormatter.Vsix

The Visual Studio extension shell. Depends on Core.

Key components:

- `GhostFormatterExtension` — Extension entry point, DI setup.
- `FormatDocumentCommand` — Ctrl+Shift+F command.
- `FormatSelectionCommand` — Ctrl+Shift+G command.
- `FormatSolutionCommand` — Solution-wide formatting.
- `FormatOnSaveHandler` — Automatic formatting on file save.
- `VsFormattingService` — Bridges VS editor API with the formatting engine.

## Adding a New Language

### Step 1: Add to the Language Enum

In `GhostFormatter.Abstractions/Model/Language.cs`:

```csharp
public enum Language
{
    // ...existing...
    TypeScript,
}
```

### Step 2: Create the Formatter

Create `GhostFormatter.Core/Formatters/TypeScript/TypeScriptFormatter.cs`:

```csharp
public sealed class TypeScriptFormatter : BaseFormatter
{
    public override Language Language => Language.TypeScript;
    public override IReadOnlyList<string> SupportedExtensions => [".ts", ".tsx"];

    public TypeScriptFormatter(ILogger<TypeScriptFormatter> logger)
        : base(logger) { }

    protected override Task<string> FormatCoreAsync(
        string text, FormattingOptions options, CancellationToken ct)
    {
        // Implement formatting logic.
        // text has already been normalized to LF line endings.
        // Return the formatted text — BaseFormatter handles:
        //   - trailing whitespace trimming
        //   - blank line collapsing
        //   - line ending conversion
        //   - final newline insertion
        return Task.FromResult(text);
    }

    // Optional: override CanFormatAsync for validation
    // Optional: override FormatSelectionCoreAsync for selection support
    // Optional: override ValidateInputAsync for pre-flight checks
}
```

### Step 3: Register in DI

In `ServiceCollectionExtensions.cs`:

```csharp
services.AddSingleton<ILanguageFormatter, TypeScriptFormatter>();
```

### Step 4: Add to LanguageDetector

In `LanguageDetector.cs`, add the extension mapping:

```csharp
[".ts"] = Language.TypeScript,
[".tsx"] = Language.TypeScript,
```

### Step 5: Write Tests

Create `tests/GhostFormatter.Core.Tests/Formatters/TypeScript/TypeScriptFormatterTests.cs`
following the pattern of existing formatter tests.

## Creating Custom Rules

Rules are composable transformations. Implement `IFormattingRule`:

```csharp
public sealed class SortCssPropertiesRule : IFormattingRule
{
    public string RuleId => "GF-CSS-001";
    public string Name => "Sort CSS Properties";
    public int Order => 100; // Higher runs later

    public Task<string> ApplyAsync(
        string text, FormattingOptions options, CancellationToken ct)
    {
        // Transform text
        return Task.FromResult(transformedText);
    }
}
```

Then apply rules within your formatter's `FormatCoreAsync`:

```csharp
foreach (var rule in _rules.OrderBy(r => r.Order))
{
    text = await rule.ApplyAsync(text, options, cancellationToken);
}
```

## Configuration Extension Points

To add a new language-specific option:

1. Create a new options class (e.g., `TypeScriptFormattingOptions`)
2. Add it as a property on `FormattingOptions`
3. Include it in `Clone()`
4. Read from `ghost-formatter.json` in `OptionsProvider`

## Semantic Safety Principles

Every formatter must follow these rules:

1. **Never change token content** — Only modify whitespace and trivia
2. **Never reorder statements** — Keep all executable code in original order
3. **Preserve string contents** — Literal strings are untouchable
4. **Preserve comment text** — Only adjust comment layout/indentation
5. **Report instead of corrupt** — If you can't format safely, emit a diagnostic

## Testing Conventions

- Unit tests go in `GhostFormatter.Core.Tests/Formatters/{Language}/`
- Integration tests go in `GhostFormatter.Integration.Tests/`
- Snapshot tests: place input files in `Snapshots/{Language}/` and verify output
- Every formatter test should verify:
  - Basic formatting works
  - Formatting is idempotent
  - String literals are preserved
  - Comments are preserved
  - Malformed input is handled gracefully
  - Cancellation is respected

## Build & Package

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release

# Run all tests
dotnet test

# Package VSIX
dotnet build src/GhostFormatter.Vsix -c Release
# Output: src/GhostFormatter.Vsix/bin/Release/GhostFormatter.Vsix.vsix
```
