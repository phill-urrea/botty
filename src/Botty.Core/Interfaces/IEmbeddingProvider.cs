namespace Botty.Core.Interfaces;

/// <summary>
/// Interface for generating text embeddings.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Embedding vector (typically 1536 dimensions for OpenAI/Claude).</returns>
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Generates embeddings for multiple texts in batch.
    /// </summary>
    /// <param name="texts">Texts to embed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of embedding vectors.</returns>
    Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken ct = default);

    /// <summary>
    /// The dimension of the embedding vectors produced.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Unique identifier for this provider (e.g. "openai", "gemini", "voyage", "placeholder").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// The model name used for embeddings (e.g. "text-embedding-3-small").
    /// </summary>
    string ModelName { get; }
}
