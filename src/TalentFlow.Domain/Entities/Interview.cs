using TalentFlow.Domain.Common;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Domain.Entities;

public class Interview : BaseEntity
{
    public Guid ApplicationId { get; set; }
    public Application Application { get; set; } = null!;

    public DateTime StartsAt { get; set; }
    public int DurationMinutes { get; set; } = 45;
    public InterviewType Type { get; set; } = InterviewType.Video;
    public string? LocationOrLink { get; set; }
    public InterviewStatus Status { get; set; } = InterviewStatus.Scheduled;

    public ICollection<InterviewParticipant> Participants { get; set; } = new List<InterviewParticipant>();
    public ICollection<InterviewFeedback> Feedbacks { get; set; } = new List<InterviewFeedback>();
}

public class InterviewParticipant
{
    public Guid InterviewId { get; set; }
    public Interview Interview { get; set; } = null!;

    public string InterviewerId { get; set; } = string.Empty;
    public string InterviewerName { get; set; } = string.Empty;
}

public class InterviewFeedback : BaseEntity
{
    public Guid InterviewId { get; set; }
    public Interview Interview { get; set; } = null!;

    public string InterviewerId { get; set; } = string.Empty;
    public string InterviewerName { get; set; } = string.Empty;

    public int Rating { get; set; } // 1 - 5
    public Recommendation Recommendation { get; set; }
    public string Comments { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
