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

namespace TalentFlow.Application.Features.Applications;

public record GetJobApplicationsQuery(Guid JobId) : IRequest<List<ApplicationCardDto>>;

public class GetJobApplicationsQueryHandler : IRequestHandler<GetJobApplicationsQuery, List<ApplicationCardDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetJobApplicationsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<List<ApplicationCardDto>> Handle(GetJobApplicationsQuery request, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId && !j.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), request.JobId);

        if (_currentUser.Role == Roles.HiringManager && job.HiringManagerId != _currentUser.UserId)
        {
            throw new ForbiddenException("Hiring managers can only view applications for assigned jobs.");
        }

        var apps = await _context.Applications
            .AsNoTracking()
            .Include(a => a.Candidate)
            .Include(a => a.CurrentStage)
            .Include(a => a.Notes)
            .Include(a => a.Interviews)
            .Include(a => a.OfferApproval)
            .Where(a => a.JobId == request.JobId)
            .OrderByDescending(a => a.AppliedAt)
            .Select(a => new ApplicationCardDto(
                a.Id,
                a.JobId,
                a.CandidateId,
                a.Candidate.FullName,
                a.Candidate.Email,
                a.Candidate.Phone,
                a.CurrentStageId,
                a.CurrentStage.Name,
                a.AppliedAt,
                a.Notes.Count,
                a.Interviews.Count,
                a.OfferApproval != null,
                Convert.ToBase64String(a.RowVersion)
            ))
            .ToListAsync(cancellationToken);

        return apps;
    }
}

public record GetApplicationByIdQuery(Guid Id) : IRequest<ApplicationDetailDto>;

public class GetApplicationByIdQueryHandler : IRequestHandler<GetApplicationByIdQuery, ApplicationDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetApplicationByIdQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<ApplicationDetailDto> Handle(GetApplicationByIdQuery request, CancellationToken cancellationToken)
    {
        var app = await _context.Applications
            .AsNoTracking()
            .Include(a => a.Job)
            .Include(a => a.Candidate)
            .Include(a => a.CurrentStage)
            .Include(a => a.StageTransitions)
                .ThenInclude(st => st.FromStage)
            .Include(a => a.StageTransitions)
                .ThenInclude(st => st.ToStage)
            .Include(a => a.Notes)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Participants)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Feedbacks)
            .Include(a => a.OfferApproval)
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(DomainApplication), request.Id);

        if (_currentUser.Role == Roles.HiringManager && app.Job.HiringManagerId != _currentUser.UserId)
        {
            throw new ForbiddenException("Hiring managers can only view applications for assigned jobs.");
        }

        return new ApplicationDetailDto(
            app.Id,
            app.JobId,
            app.Job.Title,
            app.CandidateId,
            app.Candidate.FullName,
            app.Candidate.Email,
            app.Candidate.Phone,
            app.CurrentStageId,
            app.CurrentStage.Name,
            app.ResumePath,
            app.ResumeOriginalFileName,
            app.CoverNote,
            app.AppliedAt,
            Convert.ToBase64String(app.RowVersion),
            app.StageTransitions.OrderByDescending(t => t.OccurredAt).Select(t => new StageTransitionDto(
                t.Id, t.FromStageId, t.FromStage?.Name ?? "Unknown", t.ToStageId, t.ToStage?.Name ?? "Unknown",
                t.ActorId, t.ActorName, t.Reason, t.OccurredAt
            )).ToList(),
            app.Notes.OrderByDescending(n => n.CreatedAt).Select(n => new NoteDto(
                n.Id, n.AuthorId, n.AuthorName, n.Body, n.CreatedAt
            )).ToList(),
            app.Interviews.OrderByDescending(i => i.StartsAt).Select(i => new InterviewDto(
                i.Id, i.ApplicationId, i.StartsAt, i.DurationMinutes, i.Type, i.LocationOrLink, i.Status,
                i.Participants.Select(p => p.InterviewerName).ToList(),
                i.Feedbacks.Select(f => new InterviewFeedbackDto(
                    f.Id, f.InterviewerId, f.InterviewerName, f.Rating, f.Recommendation, f.Comments, f.SubmittedAt
                )).ToList()
            )).ToList(),
            app.OfferApproval != null ? new OfferApprovalDto(
                app.OfferApproval.Id, app.OfferApproval.ApprovedById, app.OfferApproval.ApprovedByName,
                app.OfferApproval.ApprovedAt, app.OfferApproval.Notes
            ) : null
        );
    }
}

public record MoveApplicationCommand(
    Guid ApplicationId,
    Guid TargetStageId,
    string? Reason,
    string ConcurrencyToken,
    bool AllowSkip = false
) : IRequest<ApplicationCardDto>;

public class MoveApplicationCommandHandler : IRequestHandler<MoveApplicationCommand, ApplicationCardDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPipelineNotifier _notifier;

    public MoveApplicationCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IPipelineNotifier notifier)
    {
        _context = context;
        _currentUser = currentUser;
        _notifier = notifier;
    }

    public async Task<ApplicationCardDto> Handle(MoveApplicationCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch application with dependencies
        var app = await _context.Applications
            .Include(a => a.Job)
            .Include(a => a.Candidate)
            .Include(a => a.CurrentStage)
            .Include(a => a.Interviews)
                .ThenInclude(i => i.Feedbacks)
            .Include(a => a.OfferApproval)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(DomainApplication), request.ApplicationId);

        // 2. Optimistic Concurrency check (RowVersion)
        if (!string.IsNullOrWhiteSpace(request.ConcurrencyToken))
        {
            var expectedToken = Convert.FromBase64String(request.ConcurrencyToken);
            if (!app.RowVersion.SequenceEqual(expectedToken))
            {
                throw new ConflictException("The application was modified by another user. Please refresh and try again.");
            }
        }

        // 3. Fetch target stage
        var targetStage = await _context.PipelineStages.FirstOrDefaultAsync(s => s.Id == request.TargetStageId, cancellationToken)
            ?? throw new NotFoundException(nameof(PipelineStage), request.TargetStageId);

        // 4. Validate transition rules using Domain validator
        bool hasFeedback = app.Interviews.SelectMany(i => i.Feedbacks).Any();
        bool hasApproval = app.OfferApproval != null;
        var ruleCheck = StageTransitionValidator.Validate(
            app.CurrentStage,
            targetStage,
            hasInterviewFeedback: hasFeedback,
            hasOfferApproval: hasApproval,
            allowSkip: request.AllowSkip,
            userRole: _currentUser.Role ?? "");

        if (!ruleCheck.IsValid)
        {
            throw new ConflictException(ruleCheck.ErrorMessage!);
        }

        var previousStage = app.CurrentStage;
        var previousStageId = app.CurrentStageId;

        // 5. Update application stage and bump concurrency token
        app.CurrentStageId = targetStage.Id;
        app.CurrentStage = targetStage;
        app.RowVersion = Guid.NewGuid().ToByteArray();
        app.UpdatedAt = DateTime.UtcNow;

        // 6. Record StageTransition
        var actorName = _currentUser.FullName ?? _currentUser.Email ?? "Recruiter";
        var transition = new StageTransition
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            FromStageId = previousStageId,
            ToStageId = targetStage.Id,
            ActorId = _currentUser.UserId ?? "System",
            ActorName = actorName,
            Reason = request.Reason,
            OccurredAt = DateTime.UtcNow
        };
        _context.StageTransitions.Add(transition);

        // 7. Transactional Outbox: Send stage update email to candidate
        string emailSubject = targetStage.Kind switch
        {
            StageKind.Rejected => $"Update regarding your application for {app.Job.Title}",
            StageKind.Hired => $"Congratulations! Welcome to the team - {app.Job.Title}",
            _ => $"Status update on your application for {app.Job.Title}"
        };

        string emailBody = targetStage.Kind switch
        {
            StageKind.Rejected => $"Dear {app.Candidate.FullName},<br/><br/>Thank you for your interest in the {app.Job.Title} position. After careful review, we have decided to move forward with other candidates at this time.<br/><br/>Reason: {request.Reason ?? "Not specified"}<br/><br/>We wish you the best in your job search.",
            StageKind.Hired => $"Dear {app.Candidate.FullName},<br/><br/>We are thrilled to officially welcome you to the team for the {app.Job.Title} role! Our onboarding team will be in touch with next steps.",
            _ => $"Dear {app.Candidate.FullName},<br/><br/>Your application for the <strong>{app.Job.Title}</strong> role has been moved to the <strong>{targetStage.Name}</strong> stage.<br/><br/>Best regards,<br/>Talent Team"
        };

        _context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "StageChangeNotification",
            ToAddress = app.Candidate.Email,
            Subject = emailSubject,
            Body = emailBody,
            DedupeKey = $"stage-change-{app.Id}-{targetStage.Id}-{DateTime.UtcNow.Ticks}",
            Status = OutboxMessageStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        // 8. Audit Log
        _context.AuditLogs.Add(new AuditLog
        {
            ActorId = _currentUser.UserId,
            ActorEmail = _currentUser.Email,
            EntityType = "Application",
            EntityId = app.Id.ToString(),
            Action = "StageTransition",
            BeforeJson = System.Text.Json.JsonSerializer.Serialize(new { Stage = previousStage.Name }),
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { Stage = targetStage.Name, Reason = request.Reason }),
            OccurredAt = DateTime.UtcNow
        });

        // Save everything in a single atomic transaction
        await _context.SaveChangesAsync(cancellationToken);

        // 9. Real-time SignalR push to all dashboards viewing this job
        await _notifier.NotifyApplicationMoved(
            app.JobId,
            app.Id,
            previousStageId,
            targetStage.Id,
            app.Candidate.FullName,
            targetStage.Name
        );

        return new ApplicationCardDto(
            app.Id,
            app.JobId,
            app.CandidateId,
            app.Candidate.FullName,
            app.Candidate.Email,
            app.Candidate.Phone,
            app.CurrentStageId,
            targetStage.Name,
            app.AppliedAt,
            app.Notes.Count,
            app.Interviews.Count,
            app.OfferApproval != null,
            Convert.ToBase64String(app.RowVersion)
        );
    }
}

public record BulkMoveApplicationsCommand(
    List<Guid> ApplicationIds,
    Guid TargetStageId,
    string? Reason
) : IRequest<int>;

public class BulkMoveApplicationsCommandHandler : IRequestHandler<BulkMoveApplicationsCommand, int>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPipelineNotifier _notifier;

    public BulkMoveApplicationsCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IPipelineNotifier notifier)
    {
        _context = context;
        _currentUser = currentUser;
        _notifier = notifier;
    }

    public async Task<int> Handle(BulkMoveApplicationsCommand request, CancellationToken cancellationToken)
    {
        var targetStage = await _context.PipelineStages.FirstOrDefaultAsync(s => s.Id == request.TargetStageId, cancellationToken)
            ?? throw new NotFoundException(nameof(PipelineStage), request.TargetStageId);

        var apps = await _context.Applications
            .Include(a => a.Job)
            .Include(a => a.Candidate)
            .Include(a => a.CurrentStage)
            .Where(a => request.ApplicationIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        int moved = 0;
        foreach (var app in apps)
        {
            var prevStageId = app.CurrentStageId;
            app.CurrentStageId = targetStage.Id;
            app.RowVersion = Guid.NewGuid().ToByteArray();
            app.UpdatedAt = DateTime.UtcNow;

            _context.StageTransitions.Add(new StageTransition
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                FromStageId = prevStageId,
                ToStageId = targetStage.Id,
                ActorId = _currentUser.UserId ?? "System",
                ActorName = _currentUser.FullName ?? "Recruiter",
                Reason = request.Reason ?? "Bulk move",
                OccurredAt = DateTime.UtcNow
            });

            _context.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "BulkStageNotification",
                ToAddress = app.Candidate.Email,
                Subject = $"Update regarding your application for {app.Job.Title}",
                Body = $"Dear {app.Candidate.FullName},<br/><br/>Your application has been moved to {targetStage.Name}.<br/><br/>Reason: {request.Reason ?? "Status update"}",
                DedupeKey = $"bulk-move-{app.Id}-{DateTime.UtcNow.Ticks}",
                Status = OutboxMessageStatus.Pending,
                Attempts = 0,
                NextAttemptAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });

            moved++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return moved;
    }
}

public record AddNoteCommand(Guid ApplicationId, string Body) : IRequest<NoteDto>;

public class AddNoteCommandHandler : IRequestHandler<AddNoteCommand, NoteDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPipelineNotifier _notifier;

    public AddNoteCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IPipelineNotifier notifier)
    {
        _context = context;
        _currentUser = currentUser;
        _notifier = notifier;
    }

    public async Task<NoteDto> Handle(AddNoteCommand request, CancellationToken cancellationToken)
    {
        var app = await _context.Applications.FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(DomainApplication), request.ApplicationId);

        var note = new Note
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            AuthorId = _currentUser.UserId ?? "Unknown",
            AuthorName = _currentUser.FullName ?? _currentUser.Email ?? "Team Member",
            Body = request.Body.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.Notes.Add(note);
        await _context.SaveChangesAsync(cancellationToken);

        var dto = new NoteDto(note.Id, note.AuthorId, note.AuthorName, note.Body, note.CreatedAt);
        await _notifier.NotifyNoteAdded(app.JobId, app.Id, dto);

        return dto;
    }
}
