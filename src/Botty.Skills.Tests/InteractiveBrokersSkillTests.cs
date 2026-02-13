using Botty.Core.Interfaces;
using Botty.Core.Models;
using Botty.Skills.InteractiveBrokers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Botty.Skills.Tests;

public class InteractiveBrokersSkillTests
{
    [Fact]
    public async Task ListAccounts_ReturnsError_WhenSessionNotAuthenticated()
    {
        var handler = new StubHandler(_ =>
        {
            var json = """{"authenticated":false,"connected":false}""";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        });

        var skill = CreateSkill(handler);
        await InitializeAsync(skill);

        var result = await skill.ExecuteAsync(new SkillContext
        {
            ToolName = "ib_list_accounts",
            Arguments = "{}"
        });

        Assert.False(result.Success);
        Assert.Contains("not authenticated", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListAccounts_ReturnsConfiguredAccounts_WhenSessionIsAuthenticated()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/v1/api/iserver/auth/status", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"authenticated":true,"connected":true}""")
                });
            }

            if (path.EndsWith("/v1/api/portfolio/accounts", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{"accountId":"U1234567","alias":"Main","currency":"EUR"}]""")
                });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });

        var skill = CreateSkill(handler);
        await InitializeAsync(skill);

        var result = await skill.ExecuteAsync(new SkillContext
        {
            ToolName = "ib_list_accounts",
            Arguments = "{}"
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Contains("\"count\":1", result.Result, StringComparison.Ordinal);
        Assert.Contains("U1234567", result.Result, StringComparison.Ordinal);
    }

    private static InteractiveBrokersSkill CreateSkill(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(handler);
        var client = new IbClient(factory, NullLogger<IbClient>.Instance);
        return new InteractiveBrokersSkill(client, NullLogger<InteractiveBrokersSkill>.Instance);
    }

    private static async Task InitializeAsync(InteractiveBrokersSkill skill)
    {
        await skill.InitializeAsync(new SkillConfiguration
        {
            SkillId = "interactive_brokers",
            Values = new Dictionary<string, string?>
            {
                ["gateway_base_url"] = "https://ib.local",
                ["request_timeout_seconds"] = "5",
                ["use_insecure_local_tls"] = "false"
            }
        });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
