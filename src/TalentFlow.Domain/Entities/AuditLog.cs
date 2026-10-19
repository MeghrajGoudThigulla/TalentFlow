using TalentFlow.Domain.Common;

namespace TalentFlow.Domain.Entities;

public class AuditLog : BaseEntity
{
    public string? ActorId { get; set; }
    public string? ActorEmail { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
