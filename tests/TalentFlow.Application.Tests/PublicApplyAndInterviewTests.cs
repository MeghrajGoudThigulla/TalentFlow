using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Features.Interviews;
using TalentFlow.Application.Features.Public;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Persistence;
using Xunit;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Application.Tests;

public class PublicApplyAndInterviewTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IFileStorageService> _fileStorageMock;
    private readonly Mock<IPipelineNotifier> _notifierMock;
    private readonly Mock<IIdentityService> _identityServiceMock;

    public PublicApplyAndInterviewTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _fileStorageMock = new Mock<IFileStorageService>();
        _notifierMock = new Mock<IPipelineNotifier>();
        _identityServiceMock = new Mock<IIdentityService>();
    }

    [Fact]
    public async Task ApplyToJob_DuplicateEmailAndJob_ShouldThrow_ConflictException()
    {
        var stage = PipelineStage.Create("Applied", 1);
        _context.PipelineStages.Add(stage);

        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Backend Dev",
            Department = "Engineering",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "admin"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FullName = "Alex Morgan",
            Email = "alex@example.com"
        };
        _context.Candidates.Add(candidate);

        var existingApp = new DomainApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            CandidateId = candidate.Id,
            CurrentStageId = stage.Id
        };
        _context.Applications.Add(existingApp);
        await _context.SaveChangesAsync();

        _fileStorageMock.Setup(f => f.SaveResumeAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("guid.pdf", "resume.pdf"));

        var handler = new ApplyToJobCommandHandler(_context, _fileStorageMock.Object, _notifierMock.Object);

        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        var command = new ApplyToJobCommand(
            job.Id,
            "Alex Morgan",
            "alex@example.com",
            "+1234567890",
            "Cover note",
            ms,
            "resume.pdf",
            "application/pdf"
        );

        var act = async () => await handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().WithMessage("*already applied*");
    }

    [Fact]
    public async Task ScheduleInterview_With_Conflicting_Interviewer_Schedule_ShouldThrow_ConflictException()
    {
        var job = new Job { Id = Guid.NewGuid(), Title = "Software Engineer", CreatedById = "admin" };
        var candidate = new Candidate { Id = Guid.NewGuid(), FullName = "Candidate One", Email = "cand@example.com" };
        _context.Jobs.Add(job);
        _context.Candidates.Add(candidate);

        var app = new DomainApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            CandidateId = candidate.Id,
            CurrentStageId = Guid.NewGuid()
        };
        _context.Applications.Add(app);

        var existingInterview = new Interview
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            StartsAt = new DateTime(2026, 11, 1, 14, 0, 0, DateTimeKind.Utc),
            DurationMinutes = 60,
            Status = InterviewStatus.Scheduled
        };

        var participant = new InterviewParticipant
        {
            InterviewId = existingInterview.Id,
            InterviewerId = "interviewer-42",
            InterviewerName = "Interviewer Bob"
        };
        existingInterview.Participants.Add(participant);
        _context.Interviews.Add(existingInterview);
        await _context.SaveChangesAsync();

        _identityServiceMock.Setup(s => s.GetUserByIdAsync("interviewer-42"))
            .ReturnsAsync(new UserDto("interviewer-42", "bob@example.com", "Interviewer Bob", Roles.HiringManager, true, DateTime.UtcNow));

        var handler = new ScheduleInterviewCommandHandler(_context, _identityServiceMock.Object, _notifierMock.Object);

        // Try to schedule overlapping interview at 14:30
        var command = new ScheduleInterviewCommand(
            app.Id,
            new DateTime(2026, 11, 1, 14, 30, 0, DateTimeKind.Utc),
            45,
            InterviewType.Video,
            "https://meet.google.com/test",
            new List<string> { "interviewer-42" }
        );

        var act = async () => await handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().WithMessage("*conflicting interview*");
    }
}
