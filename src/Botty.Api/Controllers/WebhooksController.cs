using System.Text.Json;
using Botty.Hooks;
using Botty.Hooks.Models;
using Microsoft.AspNetCore.Mvc;

namespace Botty.Api.Controllers;

/// <summary>
/// Receives inbound webhooks and triggers hooks by ID.
/// </summary>
[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IHookRegistry _registry;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IHookRegistry registry, ILogger<WebhooksController> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Generic webhook endpoint: POST /api/webhooks/{hookId} with JSON body.
    /// The hook must exist and be enabled; it will receive the body as payload.
    /// </summary>
    [HttpPost("{hookId:guid}")]
    public async Task<IActionResult> ReceiveWebhook(Guid hookId, CancellationToken ct)
    {
        var hook = _registry.GetHook(hookId.ToString());
        if (hook == null)
        {
            _logger.LogWarning("Webhook received for unknown hook {HookId}", hookId);
            return NotFound(new { error = $"Hook '{hookId}' not found" });
        }

        JsonDocument payload;
        try
        {
            payload = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
        }
        catch
        {
            return BadRequest(new { error = "Invalid JSON body" });
        }

        var context = new HookContext
        {
            Trigger = HookTrigger.WebhookReceived,
            Timestamp = DateTime.UtcNow,
            Payload = payload
        };

        var result = await hook.ExecuteAsync(context, ct);
        if (result.Success)
            return Ok(new { success = true, output = result.Output });
        return StatusCode(500, new { success = false, error = result.Error });
    }
}
