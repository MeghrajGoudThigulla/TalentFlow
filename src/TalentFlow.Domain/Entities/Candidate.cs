using TalentFlow.Domain.Common;

namespace TalentFlow.Domain.Entities;

public class Candidate : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Source { get; set; } = "Direct Application";
    public bool IsDeleted { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
