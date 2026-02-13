namespace Botty.Channels.Services;

/// <summary>
/// Validates and normalizes poll input before sending.
/// </summary>
public class PollValidationService
{
    private const int MinOptions = 2;
    private const int MaxOptions = 12;

    /// <summary>
    /// Validates and normalizes poll input. Returns a sanitized copy.
    /// </summary>
    public PollValidationResult Validate(PollInput input, int? channelMaxOptions = null)
    {
        if (string.IsNullOrWhiteSpace(input.Question))
            return PollValidationResult.Invalid("Poll question is required.");

        // Filter out blank options and trim
        var options = input.Options
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Distinct()
            .ToList();

        if (options.Count < MinOptions)
            return PollValidationResult.Invalid($"At least {MinOptions} non-blank options are required.");

        var maxAllowed = channelMaxOptions ?? MaxOptions;
        if (options.Count > maxAllowed)
            options = options.Take(maxAllowed).ToList();

        // Bounds-check maxSelections
        var maxSelections = Math.Max(1, Math.Min(input.MaxSelections, options.Count));

        var normalized = new PollInput
        {
            Question = input.Question.Trim(),
            Options = options,
            MaxSelections = maxSelections,
            DurationHours = Math.Max(0, input.DurationHours)
        };

        return PollValidationResult.Valid(normalized);
    }
}

/// <summary>
/// Result of poll validation.
/// </summary>
public class PollValidationResult
{
    public bool IsValid { get; private init; }
    public string? Error { get; private init; }
    public PollInput? NormalizedInput { get; private init; }

    public static PollValidationResult Valid(PollInput input) => new()
    {
        IsValid = true,
        NormalizedInput = input
    };

    public static PollValidationResult Invalid(string error) => new()
    {
        IsValid = false,
        Error = error
    };
}
