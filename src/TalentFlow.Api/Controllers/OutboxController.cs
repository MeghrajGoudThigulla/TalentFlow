using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Outbox;

namespace TalentFlow.Api.Controllers;

[Route("api/outbox")]
[Authorize(Policy = "RequireAdmin")]
public class OutboxController : ApiControllerBase
{
    [HttpGet("failed")]
    public async Task<ActionResult<List<OutboxMessageDto>>> GetFailed(CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetFailedOutboxQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken = default)
    {
        await Mediator.Send(new RetryOutboxMessageCommand(id), cancellationToken);
        return NoContent();
    }
}
