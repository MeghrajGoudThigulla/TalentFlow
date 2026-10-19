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
                a.MatchScore,
                a.MatchedSkills,
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
            app.MatchScore,
            app.MatchedSkills,
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
            DedupeKey = $"stage-transition-{transition.Id}",
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
            app.MatchScore,
            app.MatchedSkills,
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

        if (targetStage.Kind != StageKind.Rejected)
        {
            throw new ConflictException("Bulk move is only permitted to the Rejected stage.");
        }

        var apps = await _context.Applications
            .Include(a => a.Job)
            .Include(a => a.Candidate)
            .Include(a => a.CurrentStage)
            .Where(a => request.ApplicationIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        int moved = 0;
        foreach (var app in apps)
        {
            var ruleCheck = StageTransitionValidator.Validate(
                app.CurrentStage,
                targetStage,
                hasInterviewFeedback: false,
                hasOfferApproval: false,
                allowSkip: false,
                userRole: _currentUser.Role ?? "");

            if (!ruleCheck.IsValid)
            {
                continue;
            }

            var prevStageId = app.CurrentStageId;
            app.CurrentStageId = targetStage.Id;
            app.RowVersion = Guid.NewGuid().ToByteArray();
            app.UpdatedAt = DateTime.UtcNow;

            var transitionId = Guid.NewGuid();
            _context.StageTransitions.Add(new StageTransition
            {
                Id = transitionId,
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
                DedupeKey = $"bulk-move-{transitionId}",
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
        var app = await _context.Applications
            .Include(a => a.Job)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(DomainApplication), request.ApplicationId);

        if (_currentUser.Role == Roles.HiringManager && app.Job.HiringManagerId != _currentUser.UserId)
        {
            throw new ForbiddenException("Hiring managers can only add notes to applications for assigned jobs.");
        }

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

public record GetOfferLetterQuery(Guid ApplicationId) : IRequest<OfferLetterDto>;

public class GetOfferLetterQueryHandler : IRequestHandler<GetOfferLetterQuery, OfferLetterDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetOfferLetterQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<OfferLetterDto> Handle(GetOfferLetterQuery request, CancellationToken cancellationToken)
    {
        var app = await _context.Applications
            .Include(a => a.Job)
            .Include(a => a.Candidate)
            .Include(a => a.OfferApproval)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(DomainApplication), request.ApplicationId);

        if (_currentUser.Role == Roles.HiringManager && app.Job.HiringManagerId != _currentUser.UserId)
        {
            throw new ForbiddenException("Hiring managers can only view offer letters for assigned jobs.");
        }

        if (app.OfferApproval == null)
        {
            throw new ConflictException("An offer letter can only be generated after offer approval has been granted.");
        }

        var annualCtc = app.Job.SalaryMax ?? app.Job.SalaryMin ?? 1800000m;
        var monthlyGross = Math.Round(annualCtc / 12m, 2);
        var baseSalary = Math.Round(monthlyGross * 0.50m, 2);
        var hra = Math.Round(monthlyGross * 0.20m, 2);
        var specialAllowance = monthlyGross - baseSalary - hra;

        var refNumber = $"TF-OFF-{DateTime.UtcNow.Year}-{app.Id.ToString()[..8].ToUpper()}";
        var joiningDate = app.OfferApproval.ApprovedAt.AddDays(14);
        var approvedBy = app.OfferApproval.ApprovedByName;

        var encodedCandidateName = System.Net.WebUtility.HtmlEncode(app.Candidate.FullName);
        var encodedJobTitle = System.Net.WebUtility.HtmlEncode(app.Job.Title);
        var encodedDepartment = System.Net.WebUtility.HtmlEncode(app.Job.Department);
        var encodedLocation = System.Net.WebUtility.HtmlEncode(app.Job.Location);
        var encodedEmploymentType = System.Net.WebUtility.HtmlEncode(app.Job.EmploymentType);
        var encodedApprovedBy = System.Net.WebUtility.HtmlEncode(approvedBy);

        var htmlDocument = $@"
<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'/>
  <title>Employment Offer Letter - {encodedCandidateName}</title>
  <style>
    body {{ font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif; color: #1e293b; padding: 40px; line-height: 1.6; max-width: 800px; margin: 0 auto; }}
    .header {{ border-bottom: 2px solid #2563eb; padding-bottom: 15px; margin-bottom: 25px; display: flex; justify-content: space-between; align-items: flex-end; }}
    .logo {{ font-size: 24px; font-weight: bold; color: #2563eb; letter-spacing: -0.5px; }}
    .ref-no {{ font-size: 13px; color: #64748b; }}
    .title {{ font-size: 20px; font-weight: bold; margin: 20px 0 10px 0; text-align: center; text-transform: uppercase; color: #0f172a; }}
    .section {{ margin-top: 20px; }}
    .section-title {{ font-size: 14px; font-weight: bold; color: #2563eb; border-bottom: 1px solid #e2e8f0; padding-bottom: 4px; margin-bottom: 10px; text-transform: uppercase; }}
    table {{ width: 100%; border-collapse: collapse; margin-top: 10px; }}
    th, td {{ padding: 8px 12px; border: 1px solid #cbd5e1; font-size: 13px; text-align: left; }}
    th {{ background: #f1f5f9; font-weight: 600; }}
    .total-row {{ font-weight: bold; background: #f8fafc; }}
    .clauses {{ font-size: 12px; color: #475569; }}
    .signatures {{ margin-top: 40px; display: flex; justify-content: space-between; }}
    .signature-block {{ width: 45%; border-top: 1px solid #94a3b8; padding-top: 8px; font-size: 13px; }}
  </style>
</head>
<body>
  <div class='header'>
    <div>
      <div class='logo'>TalentFlow ATS</div>
      <div style='font-size: 12px; color: #64748b;'>Enterprise Workforce & Recruitment Ecosystem</div>
    </div>
    <div class='ref-no'>
      <strong>Ref:</strong> {refNumber}<br/>
      <strong>Date:</strong> {app.OfferApproval.ApprovedAt:MMMM dd, yyyy}
    </div>
  </div>

  <div class='title'>Confidential Employment Offer</div>

  <p>Dear <strong>{encodedCandidateName}</strong>,</p>
  <p>On behalf of <strong>TalentFlow</strong>, we are delighted to offer you the full-time position of <strong>{encodedJobTitle}</strong> in our <strong>{encodedDepartment}</strong> department.</p>

  <div class='section'>
    <div class='section-title'>1. Position Details</div>
    <ul>
      <li><strong>Position Title:</strong> {encodedJobTitle}</li>
      <li><strong>Department:</strong> {encodedDepartment}</li>
      <li><strong>Work Location:</strong> {encodedLocation}</li>
      <li><strong>Employment Type:</strong> {encodedEmploymentType}</li>
      <li><strong>Anticipated Joining Date:</strong> {joiningDate:MMMM dd, yyyy}</li>
      <li><strong>Reporting Manager:</strong> {encodedApprovedBy}</li>
    </ul>
  </div>

  <div class='section'>
    <div class='section-title'>2. Compensation Breakdown</div>
    <table>
      <thead>
        <tr>
          <th>Salary Component</th>
          <th>Monthly (INR)</th>
          <th>Annualized (INR)</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td>Basic Salary (50%)</td>
          <td>₹{baseSalary:N2}</td>
          <td>₹{(baseSalary * 12):N2}</td>
        </tr>
        <tr>
          <td>House Rent Allowance (HRA - 20%)</td>
          <td>₹{hra:N2}</td>
          <td>₹{(hra * 12):N2}</td>
        </tr>
        <tr>
          <td>Special Allowance & Flexi Benefits</td>
          <td>₹{specialAllowance:N2}</td>
          <td>₹{(specialAllowance * 12):N2}</td>
        </tr>
        <tr class='total-row'>
          <td><strong>Cost to Company (Total CTC)</strong></td>
          <td><strong>₹{monthlyGross:N2}</strong></td>
          <td><strong>₹{annualCtc:N2}</strong></td>
        </tr>
      </tbody>
    </table>
  </div>

  <div class='section clauses'>
    <div class='section-title'>3. Terms & Employment Conditions</div>
    <p><strong>A. Background Verification (BGV):</strong> This offer is contingent upon successful verification of past credentials, educational records, identity, and employment references through our verification partners.</p>
    <p><strong>B. Intellectual Property & Confidentiality:</strong> All software, patents, documents, and technical assets developed during your employment shall remain the sole exclusive property of the company.</p>
    <p><strong>C. AI & Data Security:</strong> You agree to adhere strictly to enterprise security guidelines, confidential code protection protocols, and ethical AI deployment principles.</p>
    <p><strong>D. Notice Period:</strong> Standard notice period is 60 days following the probation period of 90 days.</p>
  </div>

  <div class='signatures'>
    <div class='signature-block'>
      <strong>Authorized Signatory</strong><br/>
      {encodedApprovedBy}<br/>
      TalentFlow Hiring Authority
    </div>
    <div class='signature-block'>
      <strong>Candidate Acceptance</strong><br/>
      {encodedCandidateName}<br/>
      Signature & Date: ___________________
    </div>
  </div>
</body>
</html>";

        return new OfferLetterDto(
            refNumber,
            app.Candidate.FullName,
            app.Candidate.Email,
            app.Job.Title,
            app.Job.Department,
            app.Job.Location,
            annualCtc,
            monthlyGross,
            baseSalary,
            hra,
            specialAllowance,
            joiningDate,
            approvedBy,
            approvedBy,
            app.OfferApproval.ApprovedAt,
            htmlDocument
        );
    }
}
