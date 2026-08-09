using GhostFormatter.Abstractions.Configuration;

namespace GhostFormatter.Abstractions.Formatters;

/// <summary>
/// Represents a single, composable formatting rule.
/// Rules are applied in order by the formatter engine.
/// </summary>
public interface IFormattingRule
{
    /// <summary>Gets the unique identifier for this rule.</summary>
    string RuleId { get; }

    /// <summary>Gets the display name for diagnostics and configuration.</summary>
    string Name { get; }

    /// <summary>Gets the order in which this rule should be applied. Lower numbers run first.</summary>
    int Order { get; }

    /// <summary>
    /// Applies the formatting rule to the source text.
    /// </summary>
    /// <param name="text">The current text (may have been modified by earlier rules).</param>
    /// <param name="options">The formatting options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The text after this rule has been applied.</returns>
    Task<string> ApplyAsync(string text, FormattingOptions options, CancellationToken cancellationToken = default);
}
