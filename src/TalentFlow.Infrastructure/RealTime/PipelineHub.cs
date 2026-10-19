using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Infrastructure.RealTime;

[Authorize]
public class PipelineHub : Hub
{
    private readonly IApplicationDbContext _context;

    public PipelineHub(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task JoinJobBoard(string jobId)
    {
        var user = Context.User;
        var role = user?.FindFirst(ClaimTypes.Role)?.Value;
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user?.FindFirst("sub")?.Value;

        if (role == Roles.Admin || role == Roles.Recruiter)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"job-{jobId}");
            return;
        }

        if (role == Roles.HiringManager && Guid.TryParse(jobId, out var parsedJobId))
        {
            var job = await _context.Jobs
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == parsedJobId && !j.IsDeleted);

            if (job != null && job.HiringManagerId == userId)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"job-{jobId}");
                return;
            }
        }

        throw new HubException("Forbidden: You do not have permission to join this job board.");
    }

    public async Task LeaveJobBoard(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job-{jobId}");
    }
}

public class PipelineNotifier : IPipelineNotifier
{
    private readonly IHubContext<PipelineHub> _hubContext;

    public PipelineNotifier(IHubContext<PipelineHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyApplicationCreated(Guid jobId, object applicationSummary)
    {
        await _hubContext.Clients.Group($"job-{jobId}")
            .SendAsync("ApplicationCreated", applicationSummary);
    }

    public async Task NotifyApplicationMoved(
        Guid jobId,
        Guid applicationId,
        Guid fromStageId,
        Guid toStageId,
        string candidateName,
        string toStageName)
    {
        await _hubContext.Clients.Group($"job-{jobId}")
            .SendAsync("ApplicationMoved", new
            {
                ApplicationId = applicationId,
                JobId = jobId,
                FromStageId = fromStageId,
                ToStageId = toStageId,
                CandidateName = candidateName,
                ToStageName = toStageName,
                Timestamp = DateTime.UtcNow
            });
    }

    public async Task NotifyNoteAdded(Guid jobId, Guid applicationId, object noteSummary)
    {
        await _hubContext.Clients.Group($"job-{jobId}")
            .SendAsync("NoteAdded", new { JobId = jobId, ApplicationId = applicationId, Note = noteSummary });
    }

    public async Task NotifyInterviewScheduled(Guid jobId, Guid applicationId, object interviewSummary)
    {
        await _hubContext.Clients.Group($"job-{jobId}")
            .SendAsync("InterviewScheduled", new { JobId = jobId, ApplicationId = applicationId, Interview = interviewSummary });
    }
}
