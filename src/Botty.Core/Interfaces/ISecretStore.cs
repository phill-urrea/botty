namespace Botty.Core.Interfaces;

/// <summary>
/// Interface for secure secret storage.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Gets a secret value by key.
    /// </summary>
    /// <param name="key">The secret key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The secret value, or null if not found.</returns>
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Sets a secret value.
    /// </summary>
    /// <param name="key">The secret key.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetSecretAsync(string key, string value, CancellationToken ct = default);

    /// <summary>
    /// Deletes a secret.
    /// </summary>
    /// <param name="key">The secret key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteSecretAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Checks if a secret exists.
    /// </summary>
    /// <param name="key">The secret key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the secret exists.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
