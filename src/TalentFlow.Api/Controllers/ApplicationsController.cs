using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Applications;
using TalentFlow.Application.Features.Interviews;

namespace TalentFlow.Api.Controllers;

[Authorize]
public class ApplicationsController : ApiControllerBase
{
    private readonly IFileStorageService _fileStorage;

    public ApplicationsController(IFileStorageService fileStorage)
    {
        _fileStorage = fileStorage;
    }

    [HttpGet("api/jobs/{jobId:guid}/applications")]
    public async Task<ActionResult<List<ApplicationCardDto>>> GetJobApplications(Guid jobId)
    {
        var result = await Mediator.Send(new GetJobApplicationsQuery(jobId));
        return Ok(result);
    }

    [HttpGet("api/applications/{id:guid}")]
    public async Task<ActionResult<ApplicationDetailDto>> GetApplicationById(Guid id)
    {
        var result = await Mediator.Send(new GetApplicationByIdQuery(id));
        return Ok(result);
    }

    [HttpPost("api/applications/{id:guid}/move")]
    [Authorize(Policy = "RequireRecruiterOrAdmin")]
    public async Task<ActionResult<ApplicationCardDto>> MoveApplication(Guid id, [FromBody] MoveApplicationRequest request)
    {
        var command = new MoveApplicationCommand(
            id,
            request.TargetStageId,
            request.Reason,
            request.ConcurrencyToken,
            request.AllowSkip
        );

        var result = await Mediator.Send(command);
        return Ok(result);
    }

    [HttpPost("api/applications/bulk-move")]
    [Authorize(Policy = "RequireRecruiterOrAdmin")]
    public async Task<ActionResult<object>> BulkMove([FromBody] BulkMoveApplicationsCommand command)
    {
        var count = await Mediator.Send(command);
        return Ok(new { movedCount = count });
    }

    [HttpPost("api/applications/{id:guid}/notes")]
    [Authorize(Policy = "RequireHiringManagerOrAbove")]
    public async Task<ActionResult<NoteDto>> AddNote(Guid id, [FromBody] AddNoteRequest request)
    {
        var result = await Mediator.Send(new AddNoteCommand(id, request.Body));
        return Ok(result);
    }

    [HttpGet("api/applications/{id:guid}/resume")]
    public async Task<IActionResult> DownloadResume(Guid id)
    {
        var app = await Mediator.Send(new GetApplicationByIdQuery(id));
        if (string.IsNullOrEmpty(app.ResumePath))
        {
            return NotFound("No resume found for this application.");
        }

        var fileResult = await _fileStorage.GetResumeAsync(app.ResumePath);
        if (fileResult == null)
        {
            return NotFound("Resume file is not available on disk.");
        }

        var (stream, contentType, fileName) = fileResult.Value;
        return File(stream, contentType, app.ResumeOriginalFileName ?? fileName);
    }

    [HttpPost("api/applications/{id:guid}/offer/approve")]
    [Authorize(Policy = "RequireHiringManagerOrAbove")]
    public async Task<ActionResult<OfferApprovalDto>> ApproveOffer(Guid id, [FromBody] ApproveOfferRequest request)
    {
        var result = await Mediator.Send(new ApproveOfferCommand(id, request.Notes));
        return Ok(result);
    }

    [HttpGet("api/applications/{id:guid}/offer-letter")]
    public async Task<IActionResult> GetOfferLetter(Guid id, [FromQuery] bool rawHtml = false)
    {
        var offerLetter = await Mediator.Send(new GetOfferLetterQuery(id));
        if (rawHtml)
        {
            return Content(offerLetter.HtmlDocument, "text/html");
        }
        return Ok(offerLetter);
    }
}

public record MoveApplicationRequest(Guid TargetStageId, string? Reason, string ConcurrencyToken, bool AllowSkip = false);
public record AddNoteRequest(string Body);
public record ApproveOfferRequest(string? Notes);
