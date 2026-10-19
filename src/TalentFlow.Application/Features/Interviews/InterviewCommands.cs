using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Application.Features.Interviews;

public record ScheduleInterviewCommand(
    Guid ApplicationId,
    DateTime StartsAt,
    int DurationMinutes,
    InterviewType Type,
    string? LocationOrLink,
    List<string> InterviewerIds
) : IRequest<InterviewDto>;

public class ScheduleInterviewCommandValidator : AbstractValidator<ScheduleInterviewCommand>
{
    public ScheduleInterviewCommandValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.DurationMinutes).GreaterThan(0).LessThanOrEqualTo(480);
        RuleFor(x => x.InterviewerIds).NotEmpty().WithMessage("At least one interviewer must be assigned.");
    }
}

public class ScheduleInterviewCommandHandler : IRequestHandler<ScheduleInterviewCommand, InterviewDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityService _identityService;
    private readonly IPipelineNotifier _notifier;

    public ScheduleInterviewCommandHandler(
        IApplicationDbContext context,
        IIdentityService identityService,
        IPipelineNotifier notifier)
    {
        _context = context;
        _identityService = identityService;
        _notifier = notifier;
    }

    public async Task<InterviewDto> Handle(ScheduleInterviewCommand request, CancellationToken cancellationToken)
    {
        var app = await _context.Applications
            .Include(a => a.Job)
            .Include(a => a.Candidate)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(DomainApplication), request.ApplicationId);

        var requestedEnd = request.StartsAt.AddMinutes(request.DurationMinutes);

        // FR-16: Prevent overlapping bookings for any assigned interviewer
        foreach (var interviewerId in request.InterviewerIds)
        {
            var hasOverlap = await _context.InterviewParticipants
                .Include(ip => ip.Interview)
                .AnyAsync(ip =>
                    ip.InterviewerId == interviewerId &&
                    ip.Interview.Status == InterviewStatus.Scheduled &&
                    ip.Interview.StartsAt < requestedEnd &&
                    ip.Interview.StartsAt.AddMinutes(ip.Interview.DurationMinutes) > request.StartsAt,
                    cancellationToken);

            if (hasOverlap)
            {
                var user = await _identityService.GetUserByIdAsync(interviewerId);
                var name = user?.FullName ?? interviewerId;
                throw new ConflictException($"Interviewer '{name}' already has a conflicting interview scheduled during this time window.");
            }
        }

        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            StartsAt = request.StartsAt,
            DurationMinutes = request.DurationMinutes,
            Type = request.Type,
            LocationOrLink = request.LocationOrLink,
            Status = InterviewStatus.Scheduled,
            CreatedAt = DateTime.UtcNow
        };

        var participantsList = new List<InterviewParticipant>();
        var participantNames = new List<string>();

        foreach (var interviewerId in request.InterviewerIds)
        {
            var user = await _identityService.GetUserByIdAsync(interviewerId);
            var name = user?.FullName ?? interviewerId;
            var participant = new InterviewParticipant
            {
                InterviewId = interview.Id,
                InterviewerId = interviewerId,
                InterviewerName = name
            };
            participantsList.Add(participant);
            participantNames.Add(name);

            // Queue outbox notification to interviewer
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                _context.OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = "InterviewerInvitation",
                    ToAddress = user.Email,
                    Subject = $"Interview Scheduled: {app.Candidate.FullName} for {app.Job.Title}",
                    Body = $"Dear {user.FullName},<br/><br/>You have been scheduled to interview <strong>{app.Candidate.FullName}</strong> for <strong>{app.Job.Title}</strong> on {request.StartsAt:g} ({request.DurationMinutes} mins).<br/>Location / Link: {request.LocationOrLink ?? "TBD"}",
                    DedupeKey = $"interview-invite-{interview.Id}-{user.Id}",
                    Status = OutboxMessageStatus.Pending,
                    Attempts = 0,
                    NextAttemptAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        interview.Participants = participantsList;
        _context.Interviews.Add(interview);

        // Queue candidate notification
        _context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "CandidateInterviewScheduled",
            ToAddress = app.Candidate.Email,
            Subject = $"Interview Scheduled: {app.Job.Title}",
            Body = $"Dear {app.Candidate.FullName},<br/><br/>Your interview for the {app.Job.Title} position has been scheduled for {request.StartsAt:g} ({request.DurationMinutes} minutes).<br/>Type: {request.Type}<br/>Details: {request.LocationOrLink ?? "Details will be sent shortly."}",
            DedupeKey = $"candidate-interview-{interview.Id}",
            Status = OutboxMessageStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        var dto = new InterviewDto(
            interview.Id, interview.ApplicationId, interview.StartsAt,
            interview.DurationMinutes, interview.Type, interview.LocationOrLink,
            interview.Status, participantNames, new List<InterviewFeedbackDto>());

        await _notifier.NotifyInterviewScheduled(app.JobId, app.Id, dto);

        return dto;
    }
}

public record SubmitFeedbackCommand(
    Guid InterviewId,
    int Rating,
    Recommendation Recommendation,
    string Comments
) : IRequest<InterviewFeedbackDto>;

public class SubmitFeedbackCommandValidator : AbstractValidator<SubmitFeedbackCommand>
{
    public SubmitFeedbackCommandValidator()
    {
        RuleFor(x => x.InterviewId).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5.");
        RuleFor(x => x.Comments).NotEmpty().WithMessage("Comments are required.");
    }
}

public class SubmitFeedbackCommandHandler : IRequestHandler<SubmitFeedbackCommand, InterviewFeedbackDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public SubmitFeedbackCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<InterviewFeedbackDto> Handle(SubmitFeedbackCommand request, CancellationToken cancellationToken)
    {
        var interview = await _context.Interviews
            .Include(i => i.Participants)
            .Include(i => i.Feedbacks)
            .FirstOrDefaultAsync(i => i.Id == request.InterviewId, cancellationToken)
            ?? throw new NotFoundException(nameof(Interview), request.InterviewId);

        var currentUserId = _currentUser.UserId;
        if (string.IsNullOrEmpty(currentUserId))
        {
            throw new ForbiddenException("Authentication required.");
        }

        // FR-17: Feedback submission only by assigned interviewers
        var isAssigned = interview.Participants.Any(p => p.InterviewerId == currentUserId);
        if (!isAssigned && _currentUser.Role != Roles.Admin)
        {
            throw new ForbiddenException("Only assigned interviewers can submit feedback for this interview.");
        }

        // FR-17: One feedback per interviewer per interview
        var existing = interview.Feedbacks.Any(f => f.InterviewerId == currentUserId);
        if (existing)
        {
            throw new ConflictException("You have already submitted feedback for this interview.");
        }

        var feedback = new InterviewFeedback
        {
            Id = Guid.NewGuid(),
            InterviewId = interview.Id,
            InterviewerId = currentUserId,
            InterviewerName = _currentUser.FullName ?? _currentUser.Email ?? "Interviewer",
            Rating = request.Rating,
            Recommendation = request.Recommendation,
            Comments = request.Comments.Trim(),
            SubmittedAt = DateTime.UtcNow
        };

        _context.InterviewFeedbacks.Add(feedback);
        await _context.SaveChangesAsync(cancellationToken);

        return new InterviewFeedbackDto(
            feedback.Id, feedback.InterviewerId, feedback.InterviewerName,
            feedback.Rating, feedback.Recommendation, feedback.Comments, feedback.SubmittedAt);
    }
}

public record ApproveOfferCommand(Guid ApplicationId, string? Notes) : IRequest<OfferApprovalDto>;

public class ApproveOfferCommandValidator : AbstractValidator<ApproveOfferCommand>
{
    public ApproveOfferCommandValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();
    }
}

public class ApproveOfferCommandHandler : IRequestHandler<ApproveOfferCommand, OfferApprovalDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public ApproveOfferCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<OfferApprovalDto> Handle(ApproveOfferCommand request, CancellationToken cancellationToken)
    {
        var app = await _context.Applications
            .Include(a => a.Job)
            .Include(a => a.OfferApproval)
            .FirstOrDefaultAsync(a => a.Id == request.ApplicationId, cancellationToken)
            ?? throw new NotFoundException(nameof(DomainApplication), request.ApplicationId);

        // Only hiring manager assigned to the job (or Admin) can approve offer
        if (_currentUser.Role == Roles.HiringManager && app.Job.HiringManagerId != _currentUser.UserId)
        {
            throw new ForbiddenException("Only the designated hiring manager for this job can approve the offer.");
        }

        if (app.OfferApproval != null)
        {
            throw new ConflictException("Offer has already been approved for this application.");
        }

        var approval = new OfferApproval
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            ApprovedById = _currentUser.UserId ?? "Unknown",
            ApprovedByName = _currentUser.FullName ?? _currentUser.Email ?? "Hiring Manager",
            ApprovedAt = DateTime.UtcNow,
            Notes = request.Notes
        };

        _context.OfferApprovals.Add(approval);
        await _context.SaveChangesAsync(cancellationToken);

        return new OfferApprovalDto(
            approval.Id, approval.ApprovedById, approval.ApprovedByName,
            approval.ApprovedAt, approval.Notes);
    }
}

public record RescheduleInterviewCommand(
    Guid InterviewId,
    DateTime StartsAt,
    int DurationMinutes,
    InterviewType Type,
    string? LocationOrLink
) : IRequest<InterviewDto>;

public class RescheduleInterviewCommandValidator : AbstractValidator<RescheduleInterviewCommand>
{
    public RescheduleInterviewCommandValidator()
    {
        RuleFor(x => x.InterviewId).NotEmpty();
        RuleFor(x => x.DurationMinutes).GreaterThan(0).LessThanOrEqualTo(480);
    }
}

public class RescheduleInterviewCommandHandler : IRequestHandler<RescheduleInterviewCommand, InterviewDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly IPipelineNotifier _notifier;

    public RescheduleInterviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUser,
        IPipelineNotifier notifier)
    {
        _context = context;
        _currentUser = currentUser;
        _notifier = notifier;
    }

    public async Task<InterviewDto> Handle(RescheduleInterviewCommand request, CancellationToken cancellationToken)
    {
        var interview = await _context.Interviews
            .Include(i => i.Application)
                .ThenInclude(a => a.Job)
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Participants)
            .Include(i => i.Feedbacks)
            .FirstOrDefaultAsync(i => i.Id == request.InterviewId, cancellationToken)
            ?? throw new NotFoundException(nameof(Interview), request.InterviewId);

        if (interview.Status == InterviewStatus.Cancelled)
        {
            throw new ConflictException("Cannot reschedule an interview that has been cancelled.");
        }

        if (_currentUser.Role == Roles.HiringManager &&
            interview.Application.Job.HiringManagerId != _currentUser.UserId &&
            !interview.Participants.Any(p => p.InterviewerId == _currentUser.UserId))
        {
            throw new ForbiddenException("Hiring managers can only reschedule interviews assigned to them.");
        }

        var requestedEnd = request.StartsAt.AddMinutes(request.DurationMinutes);
        var participantIds = interview.Participants.Select(p => p.InterviewerId).ToList();

        foreach (var interviewerId in participantIds)
        {
            var hasOverlap = await _context.Interviews
                .AsNoTracking()
                .Where(i => i.Id != interview.Id && i.Status == InterviewStatus.Scheduled)
                .Where(i => i.Participants.Any(p => p.InterviewerId == interviewerId))
                .AnyAsync(i => request.StartsAt < i.StartsAt.AddMinutes(i.DurationMinutes) &&
                               requestedEnd > i.StartsAt, cancellationToken);

            if (hasOverlap)
            {
                throw new ConflictException($"Interviewer {interviewerId} is already booked for an overlapping interview.");
            }
        }

        interview.StartsAt = request.StartsAt;
        interview.DurationMinutes = request.DurationMinutes;
        interview.Type = request.Type;
        if (!string.IsNullOrWhiteSpace(request.LocationOrLink))
        {
            interview.LocationOrLink = request.LocationOrLink;
        }
        interview.Status = InterviewStatus.Scheduled;
        interview.UpdatedAt = DateTime.UtcNow;

        _context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "CandidateInterviewRescheduled",
            ToAddress = interview.Application.Candidate.Email,
            Subject = $"Interview Rescheduled: {interview.Application.Job.Title}",
            Body = $"Dear {interview.Application.Candidate.FullName},<br/><br/>Your interview for the {interview.Application.Job.Title} position has been rescheduled to {request.StartsAt:g} ({request.DurationMinutes} minutes).<br/>Type: {request.Type}<br/>Details: {interview.LocationOrLink ?? "Details will be sent shortly."}",
            DedupeKey = $"candidate-interview-reschedule-{interview.Id}-{request.StartsAt.Ticks}",
            Status = OutboxMessageStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        var participantNames = interview.Participants.Select(p => p.InterviewerName).ToList();
        var feedbacks = interview.Feedbacks.Select(f => new InterviewFeedbackDto(
            f.Id, f.InterviewerId, f.InterviewerName,
            f.Rating, f.Recommendation, f.Comments, f.SubmittedAt)).ToList();

        var dto = new InterviewDto(
            interview.Id, interview.ApplicationId, interview.StartsAt,
            interview.DurationMinutes, interview.Type, interview.LocationOrLink,
            interview.Status, participantNames, feedbacks);

        await _notifier.NotifyInterviewScheduled(interview.Application.JobId, interview.ApplicationId, dto);
        return dto;
    }
}

public record CancelInterviewCommand(Guid InterviewId, string? Reason) : IRequest<Unit>;

public class CancelInterviewCommandValidator : AbstractValidator<CancelInterviewCommand>
{
    public CancelInterviewCommandValidator()
    {
        RuleFor(x => x.InterviewId).NotEmpty();
    }
}

public class CancelInterviewCommandHandler : IRequestHandler<CancelInterviewCommand, Unit>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public CancelInterviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(CancelInterviewCommand request, CancellationToken cancellationToken)
    {
        var interview = await _context.Interviews
            .Include(i => i.Application)
                .ThenInclude(a => a.Job)
            .Include(i => i.Application)
                .ThenInclude(a => a.Candidate)
            .Include(i => i.Participants)
            .FirstOrDefaultAsync(i => i.Id == request.InterviewId, cancellationToken)
            ?? throw new NotFoundException(nameof(Interview), request.InterviewId);

        if (interview.Status == InterviewStatus.Cancelled)
        {
            throw new ConflictException("Interview is already cancelled.");
        }

        if (_currentUser.Role == Roles.HiringManager &&
            interview.Application.Job.HiringManagerId != _currentUser.UserId &&
            !interview.Participants.Any(p => p.InterviewerId == _currentUser.UserId))
        {
            throw new ForbiddenException("Hiring managers can only cancel interviews assigned to them.");
        }

        interview.Status = InterviewStatus.Cancelled;
        interview.UpdatedAt = DateTime.UtcNow;

        _context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "CandidateInterviewCancelled",
            ToAddress = interview.Application.Candidate.Email,
            Subject = $"Interview Cancelled: {interview.Application.Job.Title}",
            Body = $"Dear {interview.Application.Candidate.FullName},<br/><br/>Your interview for the {interview.Application.Job.Title} position scheduled for {interview.StartsAt:g} has been cancelled.<br/>Reason: {request.Reason ?? "Scheduling update."}",
            DedupeKey = $"candidate-interview-cancelled-{interview.Id}",
            Status = OutboxMessageStatus.Pending,
            Attempts = 0,
            NextAttemptAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
