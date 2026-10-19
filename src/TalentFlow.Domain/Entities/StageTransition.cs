using TalentFlow.Domain.Common;

namespace TalentFlow.Domain.Entities;

public class StageTransition : BaseEntity
{
    public Guid ApplicationId { get; set; }
    public Application Application { get; set; } = null!;

    public Guid FromStageId { get; set; }
    public PipelineStage FromStage { get; set; } = null!;

    public Guid ToStageId { get; set; }
    public PipelineStage ToStage { get; set; } = null!;

    public string ActorId { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
