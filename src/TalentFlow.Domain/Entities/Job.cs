using TalentFlow.Domain.Common;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Domain.Entities;

public class Job : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string EmploymentType { get; set; } = "Full-time";
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public bool IsSalaryDisclosed { get; set; } = true;
    public int Openings { get; set; } = 1;
    public JobStatus Status { get; set; } = JobStatus.Draft;
    
    public string? HiringManagerId { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public bool IsDeleted { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();

    public void Publish()
    {
        if (Status == JobStatus.Closed)
            throw new InvalidOperationException("Cannot publish a closed job.");

        Status = JobStatus.Open;
        PublishedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Close()
    {
        Status = JobStatus.Closed;
        ClosedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
