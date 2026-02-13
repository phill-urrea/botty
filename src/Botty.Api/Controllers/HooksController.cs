using System.Text.Json;
using Botty.Api.Services;
using Botty.Hooks;
using Botty.Hooks.Models;
using Microsoft.AspNetCore.Mvc;

namespace Botty.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HooksController : ControllerBase
{
    private readonly HookService _hookService;
    private readonly IHookRegistry _registry;
    private readonly ILogger<HooksController> _logger;

    public HooksController(HookService hookService, IHookRegistry registry, ILogger<HooksController> logger)
    {
        _hookService = hookService;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>List all hooks.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<HookListDto>>> List(CancellationToken ct)
    {
        var list = await _hookService.GetAllAsync(ct);
        return Ok(list);
    }

    /// <summary>Get hook by ID.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<HookDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var hook = await _hookService.GetByIdAsync(id, ct);
        if (hook == null) return NotFound();
        return Ok(hook);
    }

    /// <summary>Create a new hook.</summary>
    [HttpPost]
    public async Task<ActionResult<HookDetailDto>> Create([FromBody] CreateHookRequest request, CancellationToken ct)
    {
        var created = await _hookService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Update a hook.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<HookDetailDto>> Update(Guid id, [FromBody] UpdateHookRequest request, CancellationToken ct)
    {
        var updated = await _hookService.UpdateAsync(id, request, ct);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    /// <summary>Delete a hook.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!await _hookService.DeleteAsync(id, ct)) return NotFound();
        return NoContent();
    }

    /// <summary>Test hook with a sample payload.</summary>
    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<HookResult>> Test(Guid id, [FromBody] JsonDocument? payload, CancellationToken ct)
    {
        var hook = _registry.GetHook(id.ToString());
        if (hook == null) return NotFound();
        var doc = payload ?? JsonDocument.Parse("{}");
        var context = new HookContext
        {
            Trigger = HookTrigger.WebhookReceived,
            Timestamp = DateTime.UtcNow,
            Payload = doc
        };
        var result = await hook.ExecuteAsync(context, ct);
        return Ok(result);
    }

    /// <summary>Enable a hook.</summary>
    [HttpPost("{id:guid}/enable")]
    public async Task<ActionResult<HookDetailDto>> Enable(Guid id, CancellationToken ct)
    {
        var updated = await _hookService.UpdateAsync(id, new UpdateHookRequest { IsEnabled = true }, ct);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    /// <summary>Disable a hook.</summary>
    [HttpPost("{id:guid}/disable")]
    public async Task<ActionResult<HookDetailDto>> Disable(Guid id, CancellationToken ct)
    {
        var updated = await _hookService.UpdateAsync(id, new UpdateHookRequest { IsEnabled = false }, ct);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    /// <summary>Get execution logs for a hook.</summary>
    [HttpGet("{id:guid}/logs")]
    public async Task<ActionResult<IEnumerable<HookExecutionDto>>> Logs(Guid id, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var logs = await _hookService.GetExecutionsAsync(id, limit, ct);
        return Ok(logs);
    }

    /// <summary>List available trigger types.</summary>
    [HttpGet("triggers")]
    public ActionResult<IEnumerable<string>> Triggers()
    {
        var triggers = Enum.GetNames(typeof(HookTrigger));
        return Ok(triggers);
    }
}
