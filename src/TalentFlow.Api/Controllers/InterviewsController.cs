using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Interviews;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Api.Controllers;

[Authorize]
public class InterviewsController : ApiControllerBase
{
    [HttpPost("/api/applications/{applicationId:guid}/interviews")]
    [Authorize(Policy = "RequireRecruiterOrAdmin")]
    public async Task<ActionResult<InterviewDto>> ScheduleInterview(
        Guid applicationId,
        [FromBody] ScheduleInterviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new ScheduleInterviewCommand(
            applicationId,
            request.StartsAt,
            request.DurationMinutes,
            request.Type,
            request.LocationOrLink,
            request.InterviewerIds
        );

        var result = await Mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/feedback")]
    [Authorize(Policy = "RequireHiringManagerOrAbove")]
    public async Task<ActionResult<InterviewFeedbackDto>> SubmitFeedback(
        Guid id,
        [FromBody] SubmitFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new SubmitFeedbackCommand(id, request.Rating, request.Recommendation, request.Comments);
        var result = await Mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireHiringManagerOrAbove")]
    public async Task<ActionResult<InterviewDto>> RescheduleInterview(
        Guid id,
        [FromBody] RescheduleInterviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new RescheduleInterviewCommand(
            id,
            request.StartsAt,
            request.DurationMinutes,
            request.Type,
            request.LocationOrLink
        );

        var result = await Mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireHiringManagerOrAbove")]
    public async Task<IActionResult> CancelInterview(
        Guid id,
        [FromQuery] string? reason,
        CancellationToken cancellationToken = default)
    {
        await Mediator.Send(new CancelInterviewCommand(id, reason), cancellationToken);
        return NoContent();
    }
}

public record ScheduleInterviewRequest(
    DateTime StartsAt,
    int DurationMinutes,
    InterviewType Type,
    string? LocationOrLink,
    List<string> InterviewerIds
);

public record RescheduleInterviewRequest(
    DateTime StartsAt,
    int DurationMinutes,
    InterviewType Type,
    string? LocationOrLink
);

public record SubmitFeedbackRequest(
    int Rating,
    Recommendation Recommendation,
    string Comments
);
