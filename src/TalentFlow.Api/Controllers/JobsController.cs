using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Jobs.Commands;
using TalentFlow.Application.Features.Jobs.Queries;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Api.Controllers;

[Route("api/jobs")]
[Authorize]
public class JobsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedList<JobDto>>> GetJobs(
        [FromQuery] string? search,
        [FromQuery] string? department,
        [FromQuery] JobStatus? status,
        [FromQuery] string? location,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await Mediator.Send(new GetJobsQuery(search, department, status, location, page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobDto>> GetJobById(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await Mediator.Send(new GetJobByIdQuery(id), cancellationToken);
        return Ok(job);
    }

    [HttpPost]
    [Authorize(Policy = "RequireRecruiterOrAdmin")]
    public async Task<ActionResult<JobDto>> CreateJob([FromBody] CreateJobCommand command, CancellationToken cancellationToken = default)
    {
        var job = await Mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetJobById), new { id = job.Id }, job);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireHiringManagerOrAbove")]
    public async Task<ActionResult<JobDto>> UpdateJob(Guid id, [FromBody] UpdateJobCommand command, CancellationToken cancellationToken = default)
    {
        if (id != command.Id)
        {
            return BadRequest("Job ID in URL does not match request body.");
        }
        var job = await Mediator.Send(command, cancellationToken);
        return Ok(job);
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = "RequireRecruiterOrAdmin")]
    public async Task<ActionResult<JobDto>> PublishJob(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await Mediator.Send(new PublishJobCommand(id), cancellationToken);
        return Ok(job);
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = "RequireRecruiterOrAdmin")]
    public async Task<ActionResult<JobDto>> CloseJob(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await Mediator.Send(new CloseJobCommand(id), cancellationToken);
        return Ok(job);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> DeleteJob(Guid id, CancellationToken cancellationToken = default)
    {
        await Mediator.Send(new DeleteJobCommand(id), cancellationToken);
        return NoContent();
    }
}
