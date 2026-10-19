using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Candidates;

namespace TalentFlow.Api.Controllers;

[Route("api/candidates")]
[Authorize(Policy = "RequireRecruiterOrAdmin")]
public class CandidatesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedList<CandidateDto>>> SearchCandidates(
        [FromQuery] string? search,
        [FromQuery] Guid? stageId,
        [FromQuery] Guid? jobId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await Mediator.Send(new SearchCandidatesQuery(search, stageId, jobId, page, pageSize));
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CandidateDto>> GetCandidateById(Guid id)
    {
        var result = await Mediator.Send(new GetCandidateByIdQuery(id));
        return Ok(result);
    }
}
