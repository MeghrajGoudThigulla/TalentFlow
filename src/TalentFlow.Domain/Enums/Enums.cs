namespace TalentFlow.Domain.Enums;

public enum JobStatus
{
    Draft = 0,
    Open = 1,
    Closed = 2
}

public enum StageKind
{
    Normal = 0,
    Hired = 1,
    Rejected = 2
}

public enum InterviewType
{
    Phone = 0,
    Video = 1,
    Onsite = 2
}

public enum InterviewStatus
{
    Scheduled = 0,
    Completed = 1,
    Cancelled = 2
}

public enum Recommendation
{
    StrongYes = 1,
    Yes = 2,
    No = 3,
    StrongNo = 4
}

public enum OutboxMessageStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Recruiter = "Recruiter";
    public const string HiringManager = "HiringManager";
}
