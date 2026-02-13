using Botty.Core.Enums;
using Botty.Core.Models;

namespace Botty.Core.Interfaces;

/// <summary>
/// Service for storing and retrieving linked OAuth accounts.
/// </summary>
public interface ILinkedAccountService
{
    Task EnsureStoreAsync(CancellationToken ct = default);

    Task<LinkedAccount> UpsertAsync(
        OAuthProviderType provider,
        string email,
        string refreshToken,
        string? accessToken,
        string? displayName,
        string? externalAccountId,
        string? scope,
        CancellationToken ct = default);

    Task<IReadOnlyList<LinkedAccount>> GetByProviderAsync(OAuthProviderType provider, CancellationToken ct = default);

    Task<IReadOnlyList<LinkedAccount>> GetAllAsync(CancellationToken ct = default);

    Task<LinkedAccountCredential?> GetCredentialByProviderAndEmailAsync(
        OAuthProviderType provider,
        string email,
        CancellationToken ct = default);

    Task DeleteAsync(Guid accountId, CancellationToken ct = default);
}
