namespace Botty.LLM.Providers;

/// <summary>
/// Utility for sanitizing and L2-normalizing embedding vectors.
/// </summary>
public static class EmbeddingNormalizer
{
    /// <summary>
    /// Replaces NaN/Infinity values with 0 and L2-normalizes the embedding vector.
    /// </summary>
    public static float[] NormalizeEmbedding(float[] embedding)
    {
        // Sanitize: replace NaN and Infinity with 0
        for (var i = 0; i < embedding.Length; i++)
        {
            if (float.IsNaN(embedding[i]) || float.IsInfinity(embedding[i]))
            {
                embedding[i] = 0f;
            }
        }

        // L2-normalize
        var sumOfSquares = 0.0;
        for (var i = 0; i < embedding.Length; i++)
        {
            sumOfSquares += embedding[i] * (double)embedding[i];
        }

        var magnitude = Math.Sqrt(sumOfSquares);
        if (magnitude > 0)
        {
            var invMag = (float)(1.0 / magnitude);
            for (var i = 0; i < embedding.Length; i++)
            {
                embedding[i] *= invMag;
            }
        }

        return embedding;
    }
}
