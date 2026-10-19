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
    public async Task<ActionResult<PipelineReportDto>> GetPipelineReport([FromQuery] Guid jobId)
    {
        var result = await Mediator.Send(new GetPipelineReportQuery(jobId));
        return Ok(result);
    }

    [HttpGet("time-to-hire")]
    public async Task<ActionResult<List<TimeToHireReportDto>>> GetTimeToHire([FromQuery] Guid? jobId)
    {
        var result = await Mediator.Send(new GetTimeToHireReportQuery(jobId));
        return Ok(result);
    }
}
