using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Public;

namespace TalentFlow.Api.Controllers;

[Route("api/public")]
[AllowAnonymous]
public class PublicController : ApiControllerBase
{
    [HttpGet("jobs")]
    public async Task<ActionResult<PagedList<PublicJobDto>>> GetOpenJobs(
        [FromQuery] string? search,
        [FromQuery] string? department,
        [FromQuery] string? location,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var result = await Mediator.Send(new GetPublicJobsQuery(search, department, location, page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<PublicJobDto>> GetOpenJobById(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetPublicJobByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("jobs/{id:guid}/apply")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting("apply-rate-limit")]
    public async Task<ActionResult<object>> Apply(
        Guid id,
        [FromForm] string fullName,
        [FromForm] string email,
        [FromForm] string? phone,
        [FromForm] string? coverNote,
        IFormFile resume,
        CancellationToken cancellationToken = default)
    {
        if (resume == null || resume.Length == 0)
        {
            return BadRequest(new { error = "Resume file is required." });
        }

        using var stream = resume.OpenReadStream();
        var command = new ApplyToJobCommand(
            id,
            fullName,
            email,
            phone,
            coverNote,
            stream,
            resume.FileName,
            resume.ContentType
        );

        var applicationId = await Mediator.Send(command, cancellationToken);

        return Ok(new
            {
            applicationId,
            message = "Application submitted successfully. A confirmation email has been queued."
        });
    }
}
