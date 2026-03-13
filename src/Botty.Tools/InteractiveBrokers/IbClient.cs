using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Botty.Tools.InteractiveBrokers;

public sealed class IbClient
{
    private const string ApiPrefix = "/v1/api";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IbClient> _logger;

    public IbClient(IHttpClientFactory httpClientFactory, ILogger<IbClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IbSessionStatus> GetSessionStatusAsync(IbClientOptions options, CancellationToken ct)
    {
        var doc = await GetJsonAsync($"{ApiPrefix}/iserver/auth/status", options, ct);
        if (doc == null)
        {
            return new IbSessionStatus(false, false, "Unable to reach Client Portal Gateway.");
        }

        var authenticated = ReadBoolean(doc.RootElement, "authenticated")
            ?? ReadBoolean(doc.RootElement, "isAuthenticated")
            ?? false;
        var connected = ReadBoolean(doc.RootElement, "connected")
            ?? ReadBoolean(doc.RootElement, "competing")
            ?? authenticated;
        var message = ReadString(doc.RootElement, "message")
            ?? ReadString(doc.RootElement, "error")
            ?? string.Empty;

        return new IbSessionStatus(authenticated, connected, message);
    }

    public async Task<IReadOnlyList<IbAccount>> ListAccountsAsync(IbClientOptions options, CancellationToken ct)
    {
        var doc = await GetJsonAsync($"{ApiPrefix}/portfolio/accounts", options, ct)
            ?? await GetJsonAsync($"{ApiPrefix}/iserver/accounts", options, ct);
        if (doc == null)
        {
            return [];
        }

        var accounts = new List<IbAccount>();
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var parsed = ParseAccount(item);
                if (parsed != null)
                {
                    accounts.Add(parsed);
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetPropertyIgnoreCase(root, "accounts", out var accountsElement) &&
                accountsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in accountsElement.EnumerateArray())
                {
                    var parsed = ParseAccount(item);
                    if (parsed != null)
                    {
                        accounts.Add(parsed);
                    }
                }
            }
            else
            {
                var parsed = ParseAccount(root);
                if (parsed != null)
                {
                    accounts.Add(parsed);
                }
            }
        }

        return accounts
            .GroupBy(a => a.AccountId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public async Task<IReadOnlyList<IbPosition>> GetPositionsAsync(
        string accountId,
        IbClientOptions options,
        int maxResults,
        CancellationToken ct)
    {
        var doc = await GetJsonAsync($"{ApiPrefix}/portfolio/{accountId}/positions/0", options, ct)
            ?? await GetJsonAsync($"{ApiPrefix}/portfolio/{accountId}/positions", options, ct);
        if (doc == null)
        {
            return [];
        }

        var positions = new List<IbPosition>();
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var parsed = ParsePosition(item, accountId);
                if (parsed != null)
                {
                    positions.Add(parsed);
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object &&
            TryGetPropertyIgnoreCase(root, "positions", out var positionsElement) &&
            positionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in positionsElement.EnumerateArray())
            {
                var parsed = ParsePosition(item, accountId);
                if (parsed != null)
                {
                    positions.Add(parsed);
                }
            }
        }

        return positions
            .Take(Math.Max(1, maxResults))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, object?>> GetPortfolioSummaryAsync(
        string accountId,
        IbClientOptions options,
        CancellationToken ct)
    {
        var doc = await GetJsonAsync($"{ApiPrefix}/portfolio/{accountId}/summary", options, ct);
        return doc == null
            ? new Dictionary<string, object?>()
            : ToScalarDictionary(doc.RootElement);
    }

    public async Task<IReadOnlyDictionary<string, object?>> GetAccountBalancesAsync(
        string accountId,
        IbClientOptions options,
        CancellationToken ct)
    {
        var doc = await GetJsonAsync($"{ApiPrefix}/portfolio/{accountId}/ledger", options, ct);
        if (doc != null)
        {
            return ToScalarDictionary(doc.RootElement);
        }

        // Fallback when ledger is not available.
        return await GetPortfolioSummaryAsync(accountId, options, ct);
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, IbClientOptions options, CancellationToken ct)
    {
        using var client = CreateClient(options);

        HttpResponseMessage? response = null;
        var attempts = 0;
        while (attempts < 3)
        {
            attempts++;
            try
            {
                response = await client.GetAsync(path, ct);
                if (response.IsSuccessStatusCode)
                {
                    var stream = await response.Content.ReadAsStreamAsync(ct);
                    return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                }

                if (!IsTransient(response.StatusCode) || attempts >= 3)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("IB request failed: {Path} -> {StatusCode} {Body}", path, response.StatusCode, body);
                    return null;
                }
            }
            catch (TaskCanceledException)
            {
                if (attempts >= 3)
                {
                    _logger.LogWarning("IB request timed out: {Path}", path);
                    return null;
                }
            }
            catch (HttpRequestException ex)
            {
                if (attempts >= 3)
                {
                    _logger.LogWarning(ex, "IB request failed: {Path}", path);
                    return null;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300 * attempts), ct);
        }

        return null;
    }

    private HttpClient CreateClient(IbClientOptions options)
    {
        HttpClient client;
        if (options.UseInsecureLocalTls)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            client = new HttpClient(handler);
        }
        else
        {
            client = _httpClientFactory.CreateClient();
        }

        client.BaseAddress = new Uri(options.GatewayBaseUrl.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Botty/1.0 (IB Client Portal)");
        return client;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code == 429 || code is >= 500 and < 600;
    }

    private static IbAccount? ParseAccount(JsonElement element)
    {
        var accountId = ReadString(element, "accountId")
            ?? ReadString(element, "id")
            ?? ReadString(element, "account");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return null;
        }

        return new IbAccount(
            accountId,
            ReadString(element, "alias"),
            ReadString(element, "type"),
            ReadString(element, "currency") ?? ReadString(element, "baseCurrency"));
    }

    private static IbPosition? ParsePosition(JsonElement element, string fallbackAccountId)
    {
        var symbol = ReadString(element, "ticker")
            ?? ReadString(element, "symbol")
            ?? ReadString(element, "contractDesc")
            ?? ReadString(element, "description");
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        return new IbPosition(
            ReadString(element, "acctId") ?? fallbackAccountId,
            symbol,
            ReadString(element, "contractDesc") ?? string.Empty,
            ReadDecimal(element, "position") ?? 0m,
            ReadDecimal(element, "avgCost") ?? 0m,
            ReadDecimal(element, "mktPrice") ?? ReadDecimal(element, "marketPrice") ?? 0m,
            ReadDecimal(element, "mktValue") ?? ReadDecimal(element, "marketValue") ?? 0m,
            ReadDecimal(element, "unrealizedPnl") ?? ReadDecimal(element, "unrealized_pnl") ?? 0m,
            ReadString(element, "currency"));
    }

    private static IReadOnlyDictionary<string, object?> ToScalarDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return dict;
        }

        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value;
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (TryGetPropertyIgnoreCase(value, "amount", out var amountElement))
                {
                    dict[property.Name] = amountElement.ValueKind switch
                    {
                        JsonValueKind.Number => amountElement.GetDecimal(),
                        JsonValueKind.String => amountElement.GetString(),
                        _ => amountElement.ToString()
                    };
                }
                else if (TryGetPropertyIgnoreCase(value, "value", out var valueElement))
                {
                    dict[property.Name] = valueElement.ValueKind switch
                    {
                        JsonValueKind.Number => valueElement.GetDecimal(),
                        JsonValueKind.String => valueElement.GetString(),
                        _ => valueElement.ToString()
                    };
                }
                else
                {
                    dict[property.Name] = value.ToString();
                }
            }
            else
            {
                dict[property.Name] = value.ValueKind switch
                {
                    JsonValueKind.Number => value.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => value.GetString(),
                    _ => value.ToString()
                };
            }
        }

        return dict;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? ReadBoolean(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}

public sealed record IbClientOptions(
    string GatewayBaseUrl,
    int RequestTimeoutSeconds,
    bool UseInsecureLocalTls);

public sealed record IbSessionStatus(
    bool IsAuthenticated,
    bool IsConnected,
    string Message);

public sealed record IbAccount(
    string AccountId,
    string? Alias,
    string? Type,
    string? Currency);

public sealed record IbPosition(
    string AccountId,
    string Symbol,
    string ContractDescription,
    decimal Quantity,
    decimal AverageCost,
    decimal MarketPrice,
    decimal MarketValue,
    decimal UnrealizedPnl,
    string? Currency);
