using TalentFlow.Domain.Common;

namespace TalentFlow.Domain.Entities;

public class Application : BaseEntity
{
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;

    public Guid CandidateId { get; set; }
    public Candidate Candidate { get; set; } = null!;

    public Guid CurrentStageId { get; set; }
    public PipelineStage CurrentStage { get; set; } = null!;

    public string? ResumePath { get; set; }
    public string? ResumeOriginalFileName { get; set; }
    public string? CoverNote { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    // Resume Match Score (0 - 100) and extracted skills (inspired by IYOV AI scoring engine)
    public int MatchScore { get; set; } = 0;
    public string? MatchedSkills { get; set; }

    // Concurrency token for optimistic locking on board card moves
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<StageTransition> StageTransitions { get; set; } = new List<StageTransition>();
    public ICollection<Note> Notes { get; set; } = new List<Note>();
    public ICollection<Interview> Interviews { get; set; } = new List<Interview>();
    public OfferApproval? OfferApproval { get; set; }
}
