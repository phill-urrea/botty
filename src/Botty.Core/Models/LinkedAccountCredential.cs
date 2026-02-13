namespace Botty.Core.Models;

/// <summary>
/// Linked account with decrypted token values loaded from secret store.
/// </summary>
public class LinkedAccountCredential
{
    public required LinkedAccount Account { get; set; }
    public required string RefreshToken { get; set; }
    public string? AccessToken { get; set; }
}
