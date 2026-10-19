using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Reports;

namespace TalentFlow.Api.Controllers;

[Route("api/reports")]
[Authorize(Policy = "RequireRecruiterOrAdmin")]
public class ReportsController : ApiControllerBase
{
    [HttpGet("pipeline")]
    public async Task<ActionResult<PipelineReportDto>> GetPipelineReport([FromQuery] Guid jobId, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPipelineReportQuery(jobId), cancellationToken);
        return Ok(result);
    }

    [HttpGet("time-to-hire")]
    public async Task<ActionResult<List<TimeToHireReportDto>>> GetTimeToHire([FromQuery] Guid? jobId, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetTimeToHireReportQuery(jobId), cancellationToken);
        return Ok(result);
    }
}
