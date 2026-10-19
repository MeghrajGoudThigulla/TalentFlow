using TalentFlow.Domain.Common;

namespace TalentFlow.Domain.Events;

public record ApplicationReceivedEvent(Guid ApplicationId, Guid JobId, string CandidateName, string CandidateEmail, string JobTitle) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record ApplicationStageChangedEvent(Guid ApplicationId, Guid JobId, string CandidateEmail, string CandidateName, string FromStageName, string ToStageName, string JobTitle, string? Reason) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public record InterviewScheduledEvent(Guid InterviewId, Guid ApplicationId, string CandidateEmail, string CandidateName, DateTime StartsAt, int DurationMinutes, string Type, string? LocationOrLink) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
