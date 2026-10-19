using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Audit;

namespace TalentFlow.Api.Controllers;

[Route("api/audit")]
[Authorize(Policy = "RequireAdmin")]
public class AuditController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedList<AuditLogDto>>> GetAuditLogs(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await Mediator.Send(new GetAuditLogsQuery(entityType, entityId, page, pageSize), cancellationToken);
        return Ok(result);
    }
}
