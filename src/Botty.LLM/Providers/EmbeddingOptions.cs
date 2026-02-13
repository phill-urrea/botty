namespace Botty.LLM.Providers;

/// <summary>
/// Configuration options for the embedding provider system.
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// Which embedding provider to use: "openai", "gemini", "voyage", "placeholder", or "auto".
    /// Auto mode tries providers in order (openai → gemini → voyage), skipping those without API keys.
    /// </summary>
    public string Provider { get; set; } = "auto";

    /// <summary>
    /// Fallback provider if the primary fails. Default: "placeholder".
    /// </summary>
    public string Fallback { get; set; } = "placeholder";

    /// <summary>
    /// Override the model name. Empty string uses the provider's default model.
    /// </summary>
    public string Model { get; set; } = "";

    /// <summary>
    /// API key for the embedding provider. Can also be set via environment variables:
    /// OPENAI_API_KEY, GOOGLE_API_KEY, VOYAGE_API_KEY.
    /// </summary>
    public string ApiKey { get; set; } = "";
}
