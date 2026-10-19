using TalentFlow.Domain.Common;

namespace TalentFlow.Domain.Entities;

public class OfferApproval : BaseEntity
{
    public Guid ApplicationId { get; set; }
    public Application Application { get; set; } = null!;

    public string ApprovedById { get; set; } = string.Empty;
    public string ApprovedByName { get; set; } = string.Empty;
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
