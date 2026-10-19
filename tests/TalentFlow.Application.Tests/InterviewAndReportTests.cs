using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Features.Interviews;
using TalentFlow.Application.Features.Reports;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Persistence;
using Xunit;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Application.Tests;

public class InterviewAndReportTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IIdentityService> _identityServiceMock;
    private readonly Mock<IPipelineNotifier> _notifierMock;

    public InterviewAndReportTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(u => u.UserId).Returns("recruiter-1");
        _currentUserMock.Setup(u => u.Role).Returns(Roles.Recruiter);
        _currentUserMock.Setup(u => u.FullName).Returns("Jane Recruiter");

        _identityServiceMock = new Mock<IIdentityService>();
        _identityServiceMock.Setup(i => i.GetUserByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(new UserDto("interviewer-1", "interviewer@example.com", "Interviewer Name", Roles.Recruiter, true, DateTime.UtcNow));

        _notifierMock = new Mock<IPipelineNotifier>();
    }

    [Fact]
    public async Task ScheduleInterview_SavesInterviewAndInterviewers()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Backend Engineer",
            Department = "Eng",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FullName = "Jane Doe",
            Email = "jane.doe@example.com"
        };
        _context.Candidates.Add(candidate);

        var stage = PipelineStage.Create("Interview", 2);
        _context.PipelineStages.Add(stage);

        var app = new DomainApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            CandidateId = candidate.Id,
            CurrentStageId = stage.Id
        };
        _context.Applications.Add(app);
        await _context.SaveChangesAsync();

        var handler = new ScheduleInterviewCommandHandler(_context, _identityServiceMock.Object, _notifierMock.Object);
        var command = new ScheduleInterviewCommand(
            ApplicationId: app.Id,
            StartsAt: DateTime.UtcNow.AddDays(1),
            DurationMinutes: 60,
            Type: InterviewType.Video,
            LocationOrLink: "https://meet.google.com/abc-def-ghi",
            InterviewerIds: new List<string> { "interviewer-1" }
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Type.Should().Be(InterviewType.Video);
        result.LocationOrLink.Should().Be("https://meet.google.com/abc-def-ghi");

        var dbInterview = await _context.Interviews.Include(i => i.Participants).FirstOrDefaultAsync(i => i.Id == result.Id);
        dbInterview.Should().NotBeNull();
        dbInterview!.Participants.Should().HaveCount(1);
    }

    [Fact]
    public async Task SubmitFeedback_AddsFeedback_AndUpdatesInterviewStatus()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Backend Engineer",
            Department = "Eng",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FullName = "Jane Doe",
            Email = "jane.doe@example.com"
        };
        _context.Candidates.Add(candidate);

        var stage = PipelineStage.Create("Interview", 2);
        _context.PipelineStages.Add(stage);

        var app = new DomainApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            CandidateId = candidate.Id,
            CurrentStageId = stage.Id
        };
        _context.Applications.Add(app);

        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            StartsAt = DateTime.UtcNow.AddHours(-1),
            DurationMinutes = 45,
            Type = InterviewType.Video,
            Status = InterviewStatus.Scheduled
        };
        interview.Participants.Add(new InterviewParticipant
        {
            InterviewId = interview.Id,
            InterviewerId = "recruiter-1",
            InterviewerName = "Jane Recruiter"
        });
        _context.Interviews.Add(interview);
        await _context.SaveChangesAsync();

        var handler = new SubmitFeedbackCommandHandler(_context, _currentUserMock.Object);
        var command = new SubmitFeedbackCommand(
            InterviewId: interview.Id,
            Rating: 5,
            Comments: "Exceptional system design and problem solving skills.",
            Recommendation: Recommendation.StrongYes
        );

        var feedbackDto = await handler.Handle(command, CancellationToken.None);

        feedbackDto.Should().NotBeNull();
        feedbackDto.Rating.Should().Be(5);
        feedbackDto.Recommendation.Should().Be(Recommendation.StrongYes);
    }

    [Fact]
    public async Task ApproveOffer_CreatesOfferApprovalRecord()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Engineering Manager",
            Department = "Eng",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FullName = "Alex Tech",
            Email = "alex.tech@example.com"
        };
        _context.Candidates.Add(candidate);

        var stage = PipelineStage.Create("Offer", 4);
        _context.PipelineStages.Add(stage);

        var app = new DomainApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            CandidateId = candidate.Id,
            CurrentStageId = stage.Id
        };
        _context.Applications.Add(app);
        await _context.SaveChangesAsync();

        var handler = new ApproveOfferCommandHandler(_context, _currentUserMock.Object);
        var command = new ApproveOfferCommand(app.Id, "Approved with base salary at 90th percentile.");

        var approval = await handler.Handle(command, CancellationToken.None);

        approval.Should().NotBeNull();
        approval.ApprovedByName.Should().Be("Jane Recruiter");

        var dbApproval = await _context.OfferApprovals.FirstOrDefaultAsync(a => a.ApplicationId == app.Id);
        dbApproval.Should().NotBeNull();
    }

    [Fact]
    public async Task GetPipelineReportQuery_CalculatesFunnelCounts()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Staff Engineer",
            Department = "Eng",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var stage1 = PipelineStage.Create("Applied", 1);
        var stage2 = PipelineStage.Create("Interview", 2);
        _context.PipelineStages.AddRange(stage1, stage2);

        var c1 = new Candidate { Id = Guid.NewGuid(), FullName = "C1", Email = "c1@test.com" };
        var c2 = new Candidate { Id = Guid.NewGuid(), FullName = "C2", Email = "c2@test.com" };
        _context.Candidates.AddRange(c1, c2);

        _context.Applications.Add(new DomainApplication { Id = Guid.NewGuid(), JobId = job.Id, CandidateId = c1.Id, CurrentStageId = stage1.Id });
        _context.Applications.Add(new DomainApplication { Id = Guid.NewGuid(), JobId = job.Id, CandidateId = c2.Id, CurrentStageId = stage2.Id });
        await _context.SaveChangesAsync();

        var handler = new GetPipelineReportQueryHandler(_context);
        var report = await handler.Handle(new GetPipelineReportQuery(job.Id), CancellationToken.None);

        report.Should().NotBeNull();
        report.TotalApplications.Should().Be(2);
        report.StageCounts["Applied"].Should().Be(1);
        report.StageCounts["Interview"].Should().Be(1);
    }

    [Fact]
    public async Task RescheduleInterview_UpdatesTime_AndQueuesOutboxMessage()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Backend Engineer",
            Department = "Eng",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate { Id = Guid.NewGuid(), FullName = "Candidate Reschedule", Email = "reschedule@test.com" };
        _context.Candidates.Add(candidate);

        var stage = PipelineStage.Create("Interview", 2);
        _context.PipelineStages.Add(stage);

        var app = new DomainApplication { Id = Guid.NewGuid(), JobId = job.Id, CandidateId = candidate.Id, CurrentStageId = stage.Id };
        _context.Applications.Add(app);

        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            StartsAt = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 45,
            Type = InterviewType.Video,
            Status = InterviewStatus.Scheduled
        };
        interview.Participants.Add(new InterviewParticipant { InterviewId = interview.Id, InterviewerId = "recruiter-1", InterviewerName = "Jane Recruiter" });
        _context.Interviews.Add(interview);
        await _context.SaveChangesAsync();

        var handler = new RescheduleInterviewCommandHandler(_context, _currentUserMock.Object, _notifierMock.Object);
        var newStartsAt = DateTime.UtcNow.AddDays(3);
        var command = new RescheduleInterviewCommand(
            InterviewId: interview.Id,
            StartsAt: newStartsAt,
            DurationMinutes: 60,
            Type: InterviewType.Video,
            LocationOrLink: "https://meet.google.com/new-link"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.DurationMinutes.Should().Be(60);
        result.LocationOrLink.Should().Be("https://meet.google.com/new-link");

        var dbInterview = await _context.Interviews.FindAsync(interview.Id);
        dbInterview!.StartsAt.Should().Be(newStartsAt);

        var outbox = await _context.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "CandidateInterviewRescheduled");
        outbox.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelInterview_SetsStatusCancelled_AndQueuesOutboxMessage()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Backend Engineer",
            Department = "Eng",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate { Id = Guid.NewGuid(), FullName = "Candidate Cancel", Email = "cancel@test.com" };
        _context.Candidates.Add(candidate);

        var stage = PipelineStage.Create("Interview", 2);
        _context.PipelineStages.Add(stage);

        var app = new DomainApplication { Id = Guid.NewGuid(), JobId = job.Id, CandidateId = candidate.Id, CurrentStageId = stage.Id };
        _context.Applications.Add(app);

        var interview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            StartsAt = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 45,
            Type = InterviewType.Video,
            Status = InterviewStatus.Scheduled
        };
        interview.Participants.Add(new InterviewParticipant { InterviewId = interview.Id, InterviewerId = "recruiter-1", InterviewerName = "Jane Recruiter" });
        _context.Interviews.Add(interview);
        await _context.SaveChangesAsync();

        var handler = new CancelInterviewCommandHandler(_context, _currentUserMock.Object);
        var command = new CancelInterviewCommand(interview.Id, "Candidate withdrew application.");

        var unit = await handler.Handle(command, CancellationToken.None);
        unit.Should().Be(MediatR.Unit.Value);

        var dbInterview = await _context.Interviews.FindAsync(interview.Id);
        dbInterview!.Status.Should().Be(InterviewStatus.Cancelled);

        var outbox = await _context.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "CandidateInterviewCancelled");
        outbox.Should().NotBeNull();
    }
}
