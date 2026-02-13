using Botty.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Botty.LLM.Providers;

/// <summary>
/// Factory that resolves the embedding provider by name from configuration.
/// Supports "auto" mode that tries providers in order, skipping those without API keys.
/// </summary>
public class EmbeddingProviderFactory
{
    private readonly EmbeddingOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EmbeddingProviderFactory> _logger;

    public EmbeddingProviderFactory(
        IOptions<EmbeddingOptions> options,
        ILoggerFactory loggerFactory)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<EmbeddingProviderFactory>();
    }

    /// <summary>
    /// Creates the configured embedding provider, with fallback support.
    /// </summary>
    public IEmbeddingProvider Create()
    {
        var primary = ResolveProvider(_options.Provider);
        if (primary != null)
        {
            _logger.LogInformation("Using embedding provider: {Provider} ({Model})",
                primary.ProviderId, primary.ModelName);
            return primary;
        }

        _logger.LogWarning("Primary embedding provider '{Provider}' unavailable, trying fallback '{Fallback}'",
            _options.Provider, _options.Fallback);

        var fallback = ResolveProvider(_options.Fallback);
        if (fallback != null)
        {
            _logger.LogInformation("Using fallback embedding provider: {Provider} ({Model})",
                fallback.ProviderId, fallback.ModelName);
            return fallback;
        }

        _logger.LogWarning("No configured embedding providers available, using placeholder");
        return new PlaceholderEmbeddingProvider(_loggerFactory.CreateLogger<PlaceholderEmbeddingProvider>());
    }

    private IEmbeddingProvider? ResolveProvider(string providerName)
    {
        return providerName.ToLowerInvariant() switch
        {
            "auto" => TryAutoResolve(),
            "openai" => TryCreateOpenAi(),
            "gemini" => TryCreateGemini(),
            "voyage" => TryCreateVoyage(),
            "placeholder" => new PlaceholderEmbeddingProvider(
                _loggerFactory.CreateLogger<PlaceholderEmbeddingProvider>()),
            _ => null
        };
    }

    private IEmbeddingProvider? TryAutoResolve()
    {
        // Try providers in order: openai → gemini → voyage
        return TryCreateOpenAi() ?? TryCreateGemini() ?? TryCreateVoyage();
    }

    private IEmbeddingProvider? TryCreateOpenAi()
    {
        var apiKey = GetApiKey("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) return null;

        return new OpenAiEmbeddingProvider(
            apiKey,
            _loggerFactory.CreateLogger<OpenAiEmbeddingProvider>(),
            string.IsNullOrEmpty(_options.Model) ? null : _options.Model);
    }

    private IEmbeddingProvider? TryCreateGemini()
    {
        var apiKey = GetApiKey("GOOGLE_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) return null;

        return new GeminiEmbeddingProvider(
            apiKey,
            _loggerFactory.CreateLogger<GeminiEmbeddingProvider>(),
            string.IsNullOrEmpty(_options.Model) ? null : _options.Model);
    }

    private IEmbeddingProvider? TryCreateVoyage()
    {
        var apiKey = GetApiKey("VOYAGE_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) return null;

        return new VoyageEmbeddingProvider(
            apiKey,
            _loggerFactory.CreateLogger<VoyageEmbeddingProvider>(),
            string.IsNullOrEmpty(_options.Model) ? null : _options.Model);
    }

    private string? GetApiKey(string envVar)
    {
        // Check options first, then environment variable
        if (!string.IsNullOrEmpty(_options.ApiKey))
            return _options.ApiKey;

        return Environment.GetEnvironmentVariable(envVar);
    }
}
