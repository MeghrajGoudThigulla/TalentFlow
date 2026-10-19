using Microsoft.AspNetCore.SignalR;
using TalentFlow.Application.Common.Interfaces;

namespace TalentFlow.Infrastructure.RealTime;

public class PipelineHub : Hub
{
    public async Task JoinJobBoard(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"job-{jobId}");
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
