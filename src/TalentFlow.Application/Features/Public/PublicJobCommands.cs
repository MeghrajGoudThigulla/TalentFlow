using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Domain.Rules;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Application.Features.Public;

public record GetPublicJobsQuery(
    string? Search,
    string? Department,
    string? Location,
    int PageNumber = 1,
    int PageSize = 20
) : IRequest<PagedList<PublicJobDto>>;

public class GetPublicJobsQueryHandler : IRequestHandler<GetPublicJobsQuery, PagedList<PublicJobDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPublicJobsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedList<PublicJobDto>> Handle(GetPublicJobsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Jobs
            .AsNoTracking()
            .Where(j => !j.IsDeleted && j.Status == JobStatus.Open);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(j => j.Title.ToLower().Contains(search) || j.Description.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.Department))
        {
            query = query.Where(j => j.Department == request.Department.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            query = query.Where(j => j.Location == request.Location.Trim());
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var pageNumber = Math.Max(1, request.PageNumber);

        var items = await query
            .OrderByDescending(j => j.PublishedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new PublicJobDto(
                j.Id,
                j.Title,
                j.Description,
                j.Department,
                j.Location,
                j.EmploymentType,
                j.IsSalaryDisclosed ? j.SalaryMin : null,
                j.IsSalaryDisclosed ? j.SalaryMax : null,
                j.IsSalaryDisclosed,
                j.Openings,
                j.PublishedAt
            ))
            .ToListAsync(cancellationToken);

        return new PagedList<PublicJobDto>(items, totalCount, pageNumber, pageSize);
    }
}

public record GetPublicJobByIdQuery(Guid Id) : IRequest<PublicJobDto>;

public class GetPublicJobByIdQueryHandler : IRequestHandler<GetPublicJobByIdQuery, PublicJobDto>
{
    private readonly IApplicationDbContext _context;

    public GetPublicJobByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PublicJobDto> Handle(GetPublicJobByIdQuery request, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.Id && !j.IsDeleted && j.Status == JobStatus.Open, cancellationToken)
            ?? throw new NotFoundException("Open Job", request.Id);

        return new PublicJobDto(
            job.Id,
            job.Title,
            job.Description,
            job.Department,
            job.Location,
            job.EmploymentType,
            job.IsSalaryDisclosed ? job.SalaryMin : null,
            job.IsSalaryDisclosed ? job.SalaryMax : null,
            job.IsSalaryDisclosed,
            job.Openings,
            job.PublishedAt
        );
    }
}

public record ApplyToJobCommand(
    Guid JobId,
    string FullName,
    string Email,
    string? Phone,
    string? CoverNote,
    Stream ResumeStream,
    string ResumeFileName,
    string ResumeContentType
) : IRequest<Guid>;

public class ApplyToJobCommandValidator : AbstractValidator<ApplyToJobCommand>
{
    public ApplyToJobCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.ResumeFileName).NotEmpty();
        RuleFor(x => x.ResumeStream).NotNull();
    }
}

public class ApplyToJobCommandHandler : IRequestHandler<ApplyToJobCommand, Guid>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IPipelineNotifier _notifier;

    public ApplyToJobCommandHandler(
        IApplicationDbContext context,
        IFileStorageService fileStorage,
        IPipelineNotifier notifier)
    {
        _context = context;
        _fileStorage = fileStorage;
        _notifier = notifier;
    }

    public async Task<Guid> Handle(ApplyToJobCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify Job exists and is Open
        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == request.JobId && !j.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), request.JobId);

        if (job.Status != JobStatus.Open)
        {
            throw new ConflictException("Applications can only be submitted for open jobs.");
        }

        // 2. Find or create Candidate
        var email = request.Email.Trim().ToLowerInvariant();
        var candidate = await _context.Candidates.FirstOrDefaultAsync(c => c.Email.ToLower() == email, cancellationToken);
        if (candidate == null)
        {
            candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = request.FullName.Trim(),
                Email = email,
                Phone = request.Phone?.Trim(),
                Source = "Direct Application",
                CreatedAt = DateTime.UtcNow
            };
            _context.Candidates.Add(candidate);
        }
        else
        {
            // Update candidate details if provided
            candidate.FullName = request.FullName.Trim();
            if (!string.IsNullOrWhiteSpace(request.Phone))
            {
                candidate.Phone = request.Phone.Trim();
            }
        }

        // 3. Duplicate Application Check: same email + job -> 409 Conflict
        var existingApp = await _context.Applications.AnyAsync(
            a => a.JobId == request.JobId && a.CandidateId == candidate.Id, cancellationToken);

        if (existingApp)
        {
            throw new ConflictException("You have already applied for this job.");
        }

        // 4. Validate and store Resume
        var (storedPath, originalFileName) = await _fileStorage.SaveResumeAsync(
            request.ResumeStream, request.ResumeFileName, request.ResumeContentType, cancellationToken);

        // 5. Get initial Pipeline Stage: 'Applied'
        var appliedStage = await _context.PipelineStages
            .OrderBy(s => s.Order)
            .FirstOrDefaultAsync(s => s.Name == "Applied", cancellationToken)
            ?? await _context.PipelineStages.OrderBy(s => s.Order).FirstAsync(cancellationToken);

        // Calculate Resume Match Score against job requirements (inspired by IYOV AI scoring engine)
        var matchResult = ResumeScoreCalculator.CalculateScore(
            job.Title,
            job.Description,
            request.CoverNote,
            request.ResumeFileName
        );

        // 6. Create Application
        var application = new DomainApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            CandidateId = candidate.Id,
            CurrentStageId = appliedStage.Id,
            ResumePath = storedPath,
            ResumeOriginalFileName = originalFileName,
            CoverNote = request.CoverNote,
            MatchScore = matchResult.Score,
            MatchedSkills = matchResult.MatchedSkillsSummary,
            AppliedAt = DateTime.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        _context.Applications.Add(application);

        // 7. Initial StageTransition log
        _context.StageTransitions.Add(new StageTransition
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            FromStageId = appliedStage.Id,
            ToStageId = appliedStage.Id,
            ActorId = "system",
            ActorName = "Candidate (Self)",
            Reason = "Initial Application Submission",
            OccurredAt = DateTime.UtcNow
        });

        // 8. Transactional Outbox: Queue confirmation email
        _context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "ApplicationConfirmation",
            ToAddress = candidate.Email,
            Subject = $"Application Received: {job.Title}",
            Body = $"Dear {candidate.FullName},<br/><br/>Thank you for applying to the <strong>{job.Title}</strong> position at our company. We have received your application and will review it shortly.<br/><br/>Best regards,<br/>Talent Acquisition Team",
            DedupeKey = $"app-received-{application.Id}",
            Status = OutboxMessageStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        // 9. Audit Log
        _context.AuditLogs.Add(new AuditLog
        {
            ActorId = candidate.Id.ToString(),
            ActorEmail = candidate.Email,
            EntityType = "Application",
            EntityId = application.Id.ToString(),
            Action = "SubmittedApplication",
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { JobId = job.Id, JobTitle = job.Title, CandidateEmail = candidate.Email }),
            OccurredAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        // 10. Real-time SignalR push to recruiter dashboards viewing the job
        await _notifier.NotifyApplicationCreated(job.Id, new
        {
            JobId = job.Id,
            ApplicationId = application.Id,
            CandidateName = candidate.FullName,
            CandidateEmail = candidate.Email,
            JobTitle = job.Title,
            StageName = appliedStage.Name,
            AppliedAt = application.AppliedAt
        });

        return application.Id;
    }
}
