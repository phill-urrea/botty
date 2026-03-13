namespace Botty.Tools.Browser;

// Tool argument models

public sealed class NavigateArgs
{
    public string? Url { get; set; }
    public int? TimeoutSeconds { get; set; }
}

public sealed class SnapshotArgs
{
    public bool? InteractiveOnly { get; set; }
}

public sealed class ClickArgs
{
    public string? Ref { get; set; }
}

public sealed class TypeArgs
{
    public string? Ref { get; set; }
    public string? Text { get; set; }
    public bool? Submit { get; set; }
}

public sealed class ScreenshotArgs
{
    public bool? FullPage { get; set; }
}

public sealed class EvaluateArgs
{
    public string? Expression { get; set; }
}

// Ref map entry
public sealed record ElementRef(string Role, string Name, int Nth);

// Result models

public sealed record NavigateResult(string Url, string Title);

public sealed record SnapshotResult(string Snapshot, bool Truncated);

public sealed record ClickResult(string Status, string Description);

public sealed record TypeResult(string Status, string Description);

public sealed record ScreenshotResult(string Base64, int Width, int Height);

public sealed record EvaluateResult(string Value);
