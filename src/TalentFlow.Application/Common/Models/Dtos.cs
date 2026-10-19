using TalentFlow.Domain.Enums;

namespace TalentFlow.Application.Common.Models;

public record JobDto(
    Guid Id,
    string Title,
    string Description,
    string Department,
    string Location,
    string EmploymentType,
    decimal? SalaryMin,
    decimal? SalaryMax,
    bool IsSalaryDisclosed,
    int Openings,
    JobStatus Status,
    string? HiringManagerId,
    string? HiringManagerName,
    string CreatedById,
    DateTime CreatedAt,
    DateTime? PublishedAt,
    DateTime? ClosedAt,
    int TotalApplications
);

public record PublicJobDto(
    Guid Id,
    string Title,
    string Description,
    string Department,
    string Location,
    string EmploymentType,
    decimal? SalaryMin,
    decimal? SalaryMax,
    bool IsSalaryDisclosed,
    int Openings,
    DateTime? PublishedAt
);

public record PipelineStageDto(
    Guid Id,
    string Name,
    int Order,
    bool IsTerminal,
    StageKind Kind
);

public record ApplicationCardDto(
    Guid Id,
    Guid JobId,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    string? CandidatePhone,
    Guid CurrentStageId,
    string StageName,
    DateTime AppliedAt,
    int MatchScore,
    string? MatchedSkills,
    int NotesCount,
    int InterviewsCount,
    bool HasOfferApproval,
    string ConcurrencyToken
);

public record StageTransitionDto(
    Guid Id,
    Guid FromStageId,
    string FromStageName,
    Guid ToStageId,
    string ToStageName,
    string ActorId,
    string ActorName,
    string? Reason,
    DateTime OccurredAt
);

public record NoteDto(
    Guid Id,
    string AuthorId,
    string AuthorName,
    string Body,
    DateTime CreatedAt
);

public record InterviewFeedbackDto(
    Guid Id,
    string InterviewerId,
    string InterviewerName,
    int Rating,
    Recommendation Recommendation,
    string Comments,
    DateTime SubmittedAt
);

public record InterviewDto(
    Guid Id,
    Guid ApplicationId,
    DateTime StartsAt,
    int DurationMinutes,
    InterviewType Type,
    string? LocationOrLink,
    InterviewStatus Status,
    List<string> InterviewerNames,
    List<InterviewFeedbackDto> Feedbacks
);

public record OfferApprovalDto(
    Guid Id,
    string ApprovedById,
    string ApprovedByName,
    DateTime ApprovedAt,
    string? Notes
);

public record OfferLetterDto(
    string ReferenceNumber,
    string CandidateName,
    string CandidateEmail,
    string JobTitle,
    string Department,
    string Location,
    decimal AnnualCtc,
    decimal MonthlyGross,
    decimal BaseSalary,
    decimal Hra,
    decimal SpecialAllowance,
    DateTime JoiningDate,
    string ReportingManager,
    string ApprovedBy,
    DateTime ApprovedAt,
    string HtmlDocument
);

public record ApplicationDetailDto(
    Guid Id,
    Guid JobId,
    string JobTitle,
    Guid CandidateId,
    string CandidateName,
    string CandidateEmail,
    string? CandidatePhone,
    Guid CurrentStageId,
    string CurrentStageName,
    int MatchScore,
    string? MatchedSkills,
    string? ResumePath,
    string? ResumeOriginalFileName,
    string? CoverNote,
    DateTime AppliedAt,
    string ConcurrencyToken,
    List<StageTransitionDto> Transitions,
    List<NoteDto> Notes,
    List<InterviewDto> Interviews,
    OfferApprovalDto? OfferApproval
);

public record CandidateDto(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string? Source,
    DateTime CreatedAt,
    List<ApplicationCardDto> Applications
);

public record AuditLogDto(
    Guid Id,
    string? ActorId,
    string? ActorEmail,
    string EntityType,
    string EntityId,
    string Action,
    string? BeforeJson,
    string? AfterJson,
    string? CorrelationId,
    DateTime OccurredAt
);

public record OutboxMessageDto(
    Guid Id,
    string Type,
    string ToAddress,
    string Subject,
    string DedupeKey,
    OutboxMessageStatus Status,
    int Attempts,
    DateTime? NextAttemptAt,
    string? LastError,
    DateTime CreatedAt,
    DateTime? SentAt
);

public record PipelineReportDto(
    Guid JobId,
    string JobTitle,
    Dictionary<string, int> StageCounts,
    int TotalApplications
);

public record TimeToHireReportDto(
    Guid JobId,
    string JobTitle,
    double AverageDaysToHire,
    int TotalHired
);

public record AuthResponseDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string UserId,
    string Email,
    string FullName,
    string Role
);
