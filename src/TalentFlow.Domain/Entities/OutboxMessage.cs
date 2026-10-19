using TalentFlow.Domain.Common;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Domain.Entities;

public class OutboxMessage : BaseEntity
{
    public string Type { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
    public int Attempts { get; set; } = 0;
    public DateTime? NextAttemptAt { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? LockedUntil { get; set; }
}
