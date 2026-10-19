using TalentFlow.Domain.Common;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Domain.Entities;

public class PipelineStage : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsTerminal { get; set; }
    public StageKind Kind { get; set; } = StageKind.Normal;
    public bool IsDeleted { get; set; }

    public static PipelineStage Create(string name, int order, StageKind kind = StageKind.Normal, bool isTerminal = false)
    {
        return new PipelineStage
        {
            Id = Guid.NewGuid(),
            Name = name,
            Order = order,
            Kind = kind,
            IsTerminal = isTerminal,
            CreatedAt = DateTime.UtcNow
        };
    }
}
