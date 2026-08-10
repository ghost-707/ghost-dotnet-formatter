using GhostFormatter.Abstractions.Configuration;
using GhostFormatter.Abstractions.Formatters;
using GhostFormatter.Core.Configuration;
using GhostFormatter.Core.Formatters;
using GhostFormatter.Core.Formatters.CSharp;
using GhostFormatter.Core.Formatters.Css;
using GhostFormatter.Core.Formatters.Html;
using GhostFormatter.Core.Formatters.JavaScript;
using GhostFormatter.Core.Formatters.Json;
using GhostFormatter.Core.Formatters.Markdown;
using GhostFormatter.Core.Formatters.Razor;
using GhostFormatter.Core.Formatters.Sql;
using GhostFormatter.Core.Formatters.Xml;
using GhostFormatter.Core.Formatters.Yaml;
using GhostFormatter.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GhostFormatter.Core;

/// <summary>
/// Extension methods for registering ghost-dotnet-formatter services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all ghost-dotnet-formatter services with the dependency injection container.
    /// </summary>
    public static IServiceCollection AddGhostFormatter(this IServiceCollection services)
    {
        // Configuration
        services.AddSingleton<IOptionsProvider, OptionsProvider>();

        // Registry
        services.AddSingleton<IFormatterRegistry, FormatterRegistry>();

        // Main service
        services.AddSingleton<IFormattingService, FormattingService>();

        // Language formatters
        services.AddSingleton<ILanguageFormatter, CSharpFormatter>();
        services.AddSingleton<ILanguageFormatter, RazorFormatter>();
        services.AddSingleton<ILanguageFormatter, HtmlFormatter>();
        services.AddSingleton<ILanguageFormatter, CssFormatter>();
        services.AddSingleton<ILanguageFormatter, JavaScriptFormatter>();
        services.AddSingleton<ILanguageFormatter, JsonFormatter>();
        services.AddSingleton<ILanguageFormatter, YamlFormatter>();
        services.AddSingleton<ILanguageFormatter, SqlFormatter>();
        services.AddSingleton<ILanguageFormatter, XmlFormatter>();
        services.AddSingleton<ILanguageFormatter, MarkdownFormatter>();

        // Also registered under their own concrete type (in addition to ILanguageFormatter
        // above) so RazorFormatter can inject them directly to format embedded <style>/
        // <script> blocks, without depending on IFormatterRegistry — that would create a
        // circular dependency, since FormatterRegistry's constructor itself consumes every
        // ILanguageFormatter, including RazorFormatter. This creates a second singleton
        // instance of each (harmless: both formatters are stateless).
        services.AddSingleton<CssFormatter>();
        services.AddSingleton<JavaScriptFormatter>();

        // Utility services
        services.AddSingleton<LanguageDetector>();
        services.AddSingleton<LineEndingNormalizer>();
        services.AddSingleton<WhitespaceNormalizer>();

        return services;
    }
}
