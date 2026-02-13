using System.Text.RegularExpressions;

namespace Botty.Channels.WhatsApp;

/// <summary>
/// Detects and strips @botname mentions from WhatsApp message text.
/// </summary>
public static partial class WhatsAppMentionProcessor
{
    /// <summary>
    /// Result of processing mentions in a message.
    /// </summary>
    public record MentionProcessResult(bool WasMentioned, string CleanedText);

    /// <summary>
    /// Checks if the bot was mentioned and strips the mention from the text.
    /// </summary>
    /// <param name="text">The message text.</param>
    /// <param name="botPhoneNumber">The bot's phone number (without @c.us suffix).</param>
    /// <param name="botName">Optional bot display name to check for.</param>
    public static MentionProcessResult Process(string text, string? botPhoneNumber, string? botName = null)
    {
        if (string.IsNullOrEmpty(text))
            return new MentionProcessResult(false, text);

        var wasMentioned = false;
        var cleaned = text;

        // Check for @phonenumber mention (WhatsApp format: @1234567890)
        if (!string.IsNullOrEmpty(botPhoneNumber))
        {
            var phonePattern = $@"@{Regex.Escape(botPhoneNumber)}";
            if (Regex.IsMatch(cleaned, phonePattern))
            {
                wasMentioned = true;
                cleaned = Regex.Replace(cleaned, phonePattern, "").Trim();
            }
        }

        // Check for @botname mention
        if (!string.IsNullOrEmpty(botName))
        {
            var namePattern = $@"@{Regex.Escape(botName)}";
            if (Regex.IsMatch(cleaned, namePattern, RegexOptions.IgnoreCase))
            {
                wasMentioned = true;
                cleaned = Regex.Replace(cleaned, namePattern, "", RegexOptions.IgnoreCase).Trim();
            }
        }

        // Clean up multiple spaces left from mention removal
        cleaned = MultipleSpaces().Replace(cleaned, " ").Trim();

        return new MentionProcessResult(wasMentioned, cleaned);
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();
}
