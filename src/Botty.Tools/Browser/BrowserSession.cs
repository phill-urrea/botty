using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Botty.Tools.Browser;

/// <summary>
/// Manages Playwright browser lifecycle: single Chromium instance, single page, headless mode.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private readonly ILogger<BrowserSession> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;

    private Dictionary<string, ElementRef>? _currentRefs;

    // Configurable properties (set during tool initialization)
    public bool Headless { get; set; } = true;
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int MaxSnapshotLength { get; set; } = 80000;
    public bool EnableJavaScriptEval { get; set; }
    public HashSet<string> BlockedUrlPatterns { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "169.254.169.254", // Cloud metadata
        "metadata.google.internal",
        "localhost",
        "127.0.0.1",
        "0.0.0.0",
        "[::1]"
    };

    public BrowserSession(ILogger<BrowserSession> logger)
    {
        _logger = logger;
    }

    public async Task<NavigateResult> NavigateAsync(string url, int? timeoutSeconds, CancellationToken ct)
    {
        ValidateUrl(url);
        var page = await EnsurePageAsync();

        var timeout = (timeoutSeconds ?? DefaultTimeoutSeconds) * 1000;
        await page.GotoAsync(url, new PageGotoOptions
        {
            Timeout = timeout,
            WaitUntil = WaitUntilState.DOMContentLoaded
        });

        _currentRefs = null; // Invalidate refs on navigation
        _logger.LogDebug("Navigated to {Url}", page.Url);

        return new NavigateResult(page.Url, await page.TitleAsync());
    }

    public async Task<SnapshotResult> SnapshotAsync(bool interactiveOnly, CancellationToken ct)
    {
        var page = await EnsurePageAsync();

        var ariaYaml = await page.Locator("body").AriaSnapshotAsync(new LocatorAriaSnapshotOptions
        {
            Timeout = DefaultTimeoutSeconds * 1000
        });

        var (snapshot, refs, truncated) = AccessibilitySnapshotBuilder.Build(
            ariaYaml, interactiveOnly, MaxSnapshotLength);

        _currentRefs = refs;
        _logger.LogDebug("Snapshot captured: {RefCount} refs, truncated={Truncated}", refs.Count, truncated);

        return new SnapshotResult(snapshot, truncated);
    }

    public async Task<ClickResult> ClickAsync(string refId, CancellationToken ct)
    {
        EnsureRefs();
        var page = await EnsurePageAsync();
        var locator = AccessibilitySnapshotBuilder.ResolveRefToLocator(page, refId, _currentRefs!);

        await locator.ClickAsync(new LocatorClickOptions
        {
            Timeout = DefaultTimeoutSeconds * 1000
        });

        _currentRefs = null; // Invalidate — page may have changed
        return new ClickResult("clicked", $"Clicked element {refId}");
    }

    public async Task<TypeResult> TypeAsync(string refId, string text, bool submit, CancellationToken ct)
    {
        EnsureRefs();
        var page = await EnsurePageAsync();
        var locator = AccessibilitySnapshotBuilder.ResolveRefToLocator(page, refId, _currentRefs!);

        await locator.FillAsync(text, new LocatorFillOptions
        {
            Timeout = DefaultTimeoutSeconds * 1000
        });

        if (submit)
        {
            await locator.PressAsync("Enter");
        }

        _currentRefs = null;
        return new TypeResult("typed", $"Typed into element {refId}{(submit ? " and submitted" : "")}");
    }

    public async Task<ScreenshotResult> ScreenshotAsync(bool fullPage, CancellationToken ct)
    {
        var page = await EnsurePageAsync();

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = fullPage,
            Type = ScreenshotType.Png
        });

        var viewport = page.ViewportSize;
        return new ScreenshotResult(
            Convert.ToBase64String(bytes),
            viewport?.Width ?? 1280,
            viewport?.Height ?? 720);
    }

    public async Task<EvaluateResult> EvaluateAsync(string expression, CancellationToken ct)
    {
        if (!EnableJavaScriptEval)
        {
            throw new InvalidOperationException(
                "JavaScript evaluation is disabled. Enable it in the browser tool config.");
        }

        var page = await EnsurePageAsync();
        var result = await page.EvaluateAsync<object?>(expression);
        return new EvaluateResult(result?.ToString() ?? "undefined");
    }

    private async Task<IPage> EnsurePageAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_page != null && !_page.IsClosed)
            {
                return _page;
            }

            // Browser crash recovery — reinitialize if needed
            if (_browser != null && !_browser.IsConnected)
            {
                _logger.LogWarning("Browser disconnected, reinitializing");
                await DisposeInternalAsync();
            }

            if (_playwright == null)
            {
                _playwright = await Playwright.CreateAsync();
            }

            if (_browser == null || !_browser.IsConnected)
            {
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = Headless,
                    Args = ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu"]
                });
                _logger.LogInformation("Chromium launched (headless={Headless})", Headless);
            }

            _page = await _browser.NewPageAsync();
            _page.SetDefaultTimeout(DefaultTimeoutSeconds * 1000);
            _currentRefs = null;

            return _page;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Invalid URL: {url}");
        }

        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            throw new ArgumentException($"Only http/https URLs are allowed. Got: {uri.Scheme}");
        }

        var host = uri.Host;
        foreach (var pattern in BlockedUrlPatterns)
        {
            if (host.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"URL blocked by security policy: {url}");
            }
        }
    }

    private void EnsureRefs()
    {
        if (_currentRefs == null)
        {
            throw new InvalidOperationException(
                "No element refs available. Call browser_snapshot first to get current page refs.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeInternalAsync();
        _semaphore.Dispose();
    }

    private async Task DisposeInternalAsync()
    {
        if (_page != null)
        {
            try { await _page.CloseAsync(); } catch { /* best effort */ }
            _page = null;
        }

        if (_browser != null)
        {
            try { await _browser.CloseAsync(); } catch { /* best effort */ }
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        _currentRefs = null;
    }
}
