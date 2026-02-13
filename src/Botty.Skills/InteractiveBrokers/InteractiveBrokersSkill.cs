using Botty.Core.Enums;
using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Skills.Base;
using Microsoft.Extensions.Logging;

namespace Botty.Skills.InteractiveBrokers;

/// <summary>
/// Read-only Interactive Brokers portfolio skill using Client Portal Gateway.
/// </summary>
public class InteractiveBrokersSkill : BaseSkill
{
    private readonly IbClient _ibClient;
    private string _gatewayBaseUrl = "https://host.docker.internal:5000";
    private string? _defaultAccountId;
    private int _requestTimeoutSeconds = 15;
    private bool _useInsecureLocalTls = true;
    private int _defaultPositionLimit = 100;

    public InteractiveBrokersSkill(
        IbClient ibClient,
        ILogger<InteractiveBrokersSkill> logger)
        : base(logger)
    {
        _ibClient = ibClient;
    }

    public override string Id => "interactive_brokers";
    public override string Name => "Interactive Brokers";
    public override string Description => "Read-only portfolio access for Interactive Brokers accounts (accounts, balances, positions, and PnL).";

    public override SkillConfigSchema ConfigSchema => new()
    {
        SkillId = Id,
        Fields =
        [
            new ConfigField
            {
                Key = "gateway_base_url",
                Label = "Gateway Base URL",
                Description = "Base URL for Client Portal Gateway (e.g. https://host.docker.internal:5000).",
                Type = ConfigFieldType.String,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "https://host.docker.internal:5000"
            },
            new ConfigField
            {
                Key = "default_account_id",
                Label = "Default Account ID",
                Description = "Optional account ID to use when a tool call does not specify one.",
                Type = ConfigFieldType.String,
                IsSensitive = false,
                IsRequired = false
            },
            new ConfigField
            {
                Key = "request_timeout_seconds",
                Label = "Request Timeout Seconds",
                Description = "Timeout for Interactive Brokers API requests.",
                Type = ConfigFieldType.Number,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "15"
            },
            new ConfigField
            {
                Key = "use_insecure_local_tls",
                Label = "Allow Self-Signed TLS",
                Description = "Allow self-signed TLS certificates for local Client Portal Gateway.",
                Type = ConfigFieldType.Boolean,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "true"
            },
            new ConfigField
            {
                Key = "default_position_limit",
                Label = "Default Position Limit",
                Description = "Default maximum positions returned by ib_list_positions.",
                Type = ConfigFieldType.Number,
                IsSensitive = false,
                IsRequired = false,
                DefaultValue = "100"
            }
        ]
    };

    protected override Task OnInitializeAsync(CancellationToken ct)
    {
        _gatewayBaseUrl = GetConfig("gateway_base_url")?.Trim() ?? _gatewayBaseUrl;
        _defaultAccountId = GetConfig("default_account_id")?.Trim();

        if (int.TryParse(GetConfig("request_timeout_seconds"), out var timeout) && timeout > 0)
        {
            _requestTimeoutSeconds = timeout;
        }

        if (bool.TryParse(GetConfig("use_insecure_local_tls"), out var insecure))
        {
            _useInsecureLocalTls = insecure;
        }

        if (int.TryParse(GetConfig("default_position_limit"), out var defaultLimit) && defaultLimit > 0)
        {
            _defaultPositionLimit = defaultLimit;
        }

        return Task.CompletedTask;
    }

    public override IEnumerable<LlmTool> GetTools()
    {
        return
        [
            new LlmTool
            {
                Name = "ib_list_accounts",
                Description = "List Interactive Brokers accounts available to the authenticated Client Portal session.",
                ParametersSchema = """
                {
                  "type": "object",
                  "properties": {},
                  "required": []
                }
                """
            },
            new LlmTool
            {
                Name = "ib_get_portfolio_summary",
                Description = "Get a portfolio summary for an Interactive Brokers account.",
                ParametersSchema = """
                {
                  "type": "object",
                  "properties": {
                    "accountId": { "type": "string", "description": "Interactive Brokers account ID (optional)." }
                  },
                  "required": []
                }
                """
            },
            new LlmTool
            {
                Name = "ib_list_positions",
                Description = "List open positions for an Interactive Brokers account.",
                ParametersSchema = """
                {
                  "type": "object",
                  "properties": {
                    "accountId": { "type": "string", "description": "Interactive Brokers account ID (optional)." },
                    "maxResults": { "type": "integer", "description": "Maximum number of positions to return." }
                  },
                  "required": []
                }
                """
            },
            new LlmTool
            {
                Name = "ib_get_account_balances",
                Description = "Get account balances and ledger values for an Interactive Brokers account.",
                ParametersSchema = """
                {
                  "type": "object",
                  "properties": {
                    "accountId": { "type": "string", "description": "Interactive Brokers account ID (optional)." }
                  },
                  "required": []
                }
                """
            },
            new LlmTool
            {
                Name = "ib_get_unrealized_pnl",
                Description = "Get aggregated unrealized PnL from current positions.",
                ParametersSchema = """
                {
                  "type": "object",
                  "properties": {
                    "accountId": { "type": "string", "description": "Interactive Brokers account ID (optional)." }
                  },
                  "required": []
                }
                """
            }
        ];
    }

    protected override async Task<SkillResult> OnExecuteAsync(SkillContext context, CancellationToken ct)
    {
        var options = BuildClientOptions();
        var session = await _ibClient.GetSessionStatusAsync(options, ct);
        if (!session.IsAuthenticated)
        {
            return SkillResult.Fail(
                "Interactive Brokers session is not authenticated. Log in to Client Portal Gateway in browser, then retry.");
        }

        return context.ToolName switch
        {
            "ib_list_accounts" => await ListAccountsAsync(options, ct),
            "ib_get_portfolio_summary" => await GetPortfolioSummaryAsync(context.Arguments, options, ct),
            "ib_list_positions" => await ListPositionsAsync(context.Arguments, options, ct),
            "ib_get_account_balances" => await GetBalancesAsync(context.Arguments, options, ct),
            "ib_get_unrealized_pnl" => await GetUnrealizedPnlAsync(context.Arguments, options, ct),
            _ => SkillResult.Fail($"Unknown tool: {context.ToolName}")
        };
    }

    private async Task<SkillResult> ListAccountsAsync(IbClientOptions options, CancellationToken ct)
    {
        var accounts = await _ibClient.ListAccountsAsync(options, ct);
        return SkillResult.Ok(ToJson(new
        {
            source = "interactive_brokers",
            gatewayBaseUrl = _gatewayBaseUrl,
            count = accounts.Count,
            accounts = accounts.Select(a => new
            {
                accountId = a.AccountId,
                alias = a.Alias,
                type = a.Type,
                currency = a.Currency,
                isDefault = string.Equals(a.AccountId, _defaultAccountId, StringComparison.OrdinalIgnoreCase)
            })
        }));
    }

    private async Task<SkillResult> GetPortfolioSummaryAsync(
        string arguments,
        IbClientOptions options,
        CancellationToken ct)
    {
        var args = ParseArguments<AccountArgs>(arguments) ?? new AccountArgs();
        var accountId = await ResolveAccountIdAsync(args.AccountId, options, ct);
        if (accountId == null)
        {
            return SkillResult.Fail("No Interactive Brokers account found in the active session.");
        }

        var summary = await _ibClient.GetPortfolioSummaryAsync(accountId, options, ct);
        var positions = await _ibClient.GetPositionsAsync(accountId, options, 25, ct);

        return SkillResult.Ok(ToJson(new
        {
            source = "interactive_brokers",
            asOfUtc = DateTime.UtcNow,
            accountId,
            summary,
            positionsPreview = positions.Select(p => new
            {
                symbol = p.Symbol,
                quantity = p.Quantity,
                marketValue = p.MarketValue,
                currency = p.Currency,
                unrealizedPnl = p.UnrealizedPnl
            })
        }));
    }

    private async Task<SkillResult> ListPositionsAsync(
        string arguments,
        IbClientOptions options,
        CancellationToken ct)
    {
        var args = ParseArguments<PositionArgs>(arguments) ?? new PositionArgs();
        var accountId = await ResolveAccountIdAsync(args.AccountId, options, ct);
        if (accountId == null)
        {
            return SkillResult.Fail("No Interactive Brokers account found in the active session.");
        }

        var maxResults = args.MaxResults is > 0
            ? Math.Min(args.MaxResults.Value, 500)
            : _defaultPositionLimit;
        var positions = await _ibClient.GetPositionsAsync(accountId, options, maxResults, ct);

        return SkillResult.Ok(ToJson(new
        {
            source = "interactive_brokers",
            asOfUtc = DateTime.UtcNow,
            accountId,
            count = positions.Count,
            positions = positions.Select(p => new
            {
                symbol = p.Symbol,
                description = p.ContractDescription,
                quantity = p.Quantity,
                averageCost = p.AverageCost,
                marketPrice = p.MarketPrice,
                marketValue = p.MarketValue,
                unrealizedPnl = p.UnrealizedPnl,
                currency = p.Currency
            })
        }));
    }

    private async Task<SkillResult> GetBalancesAsync(
        string arguments,
        IbClientOptions options,
        CancellationToken ct)
    {
        var args = ParseArguments<AccountArgs>(arguments) ?? new AccountArgs();
        var accountId = await ResolveAccountIdAsync(args.AccountId, options, ct);
        if (accountId == null)
        {
            return SkillResult.Fail("No Interactive Brokers account found in the active session.");
        }

        var balances = await _ibClient.GetAccountBalancesAsync(accountId, options, ct);
        return SkillResult.Ok(ToJson(new
        {
            source = "interactive_brokers",
            asOfUtc = DateTime.UtcNow,
            accountId,
            balances
        }));
    }

    private async Task<SkillResult> GetUnrealizedPnlAsync(
        string arguments,
        IbClientOptions options,
        CancellationToken ct)
    {
        var args = ParseArguments<AccountArgs>(arguments) ?? new AccountArgs();
        var accountId = await ResolveAccountIdAsync(args.AccountId, options, ct);
        if (accountId == null)
        {
            return SkillResult.Fail("No Interactive Brokers account found in the active session.");
        }

        var positions = await _ibClient.GetPositionsAsync(accountId, options, 500, ct);
        var byCurrency = positions
            .GroupBy(p => p.Currency ?? "UNKNOWN")
            .Select(g => new
            {
                currency = g.Key,
                unrealizedPnl = g.Sum(x => x.UnrealizedPnl),
                marketValue = g.Sum(x => x.MarketValue)
            })
            .ToList();

        return SkillResult.Ok(ToJson(new
        {
            source = "interactive_brokers",
            asOfUtc = DateTime.UtcNow,
            accountId,
            totalPositions = positions.Count,
            totalUnrealizedPnl = positions.Sum(p => p.UnrealizedPnl),
            byCurrency
        }));
    }

    private IbClientOptions BuildClientOptions()
    {
        return new IbClientOptions(
            _gatewayBaseUrl,
            _requestTimeoutSeconds,
            _useInsecureLocalTls);
    }

    private async Task<string?> ResolveAccountIdAsync(
        string? requestedAccountId,
        IbClientOptions options,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requestedAccountId))
        {
            return requestedAccountId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_defaultAccountId))
        {
            return _defaultAccountId;
        }

        var accounts = await _ibClient.ListAccountsAsync(options, ct);
        return accounts.FirstOrDefault()?.AccountId;
    }

    private class AccountArgs
    {
        public string? AccountId { get; set; }
    }

    private sealed class PositionArgs : AccountArgs
    {
        public int? MaxResults { get; set; }
    }
}
