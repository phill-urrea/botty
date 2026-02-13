using Botty.Core.Enums;

namespace Botty.Core.Models;

/// <summary>
/// Represents a linked OAuth identity used by skills.
/// Tokens are stored in secret store paths referenced here.
/// </summary>
public class LinkedAccount
{
    public Guid Id { get; set; }
    public OAuthProviderType Provider { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }
    public string? ExternalAccountId { get; set; }
    public string? Scope { get; set; }
    public required string RefreshTokenSecretPath { get; set; }
    public string? AccessTokenSecretPath { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastLinkedAt { get; set; } = DateTime.UtcNow;
}
