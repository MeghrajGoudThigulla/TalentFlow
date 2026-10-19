using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Features.Applications;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Persistence;
using Xunit;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Application.Tests;

public class ApplicationWorkflowAndNoteTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IPipelineNotifier> _notifierMock;

    public ApplicationWorkflowAndNoteTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(u => u.UserId).Returns("recruiter-1");
        _currentUserMock.Setup(u => u.Role).Returns(Roles.Recruiter);
        _currentUserMock.Setup(u => u.FullName).Returns("Jane Recruiter");
        _currentUserMock.Setup(u => u.Email).Returns("jane@example.com");

        _notifierMock = new Mock<IPipelineNotifier>();
    }

    [Fact]
    public async Task AddNote_CreatesNote_AndSavesToApplication()
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
            FullName = "Alex Developer",
            Email = "alex@example.com"
        };
        _context.Candidates.Add(candidate);

        var stage = PipelineStage.Create("Screening", 1);
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

        var handler = new AddNoteCommandHandler(_context, _currentUserMock.Object, _notifierMock.Object);
        var command = new AddNoteCommand(app.Id, "Candidate demonstrated strong distributed systems design knowledge.");

        var noteDto = await handler.Handle(command, CancellationToken.None);

        noteDto.Should().NotBeNull();
        noteDto.Body.Should().Be("Candidate demonstrated strong distributed systems design knowledge.");
        noteDto.AuthorName.Should().Be("Jane Recruiter");

        var dbNote = await _context.Notes.FirstOrDefaultAsync(n => n.ApplicationId == app.Id);
        dbNote.Should().NotBeNull();
        dbNote!.Body.Should().Be("Candidate demonstrated strong distributed systems design knowledge.");
    }

    [Fact]
    public async Task GetOfferLetter_GeneratesOfferDocument_WhenOfferApproved()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "DevOps Engineer",
            Department = "Infra",
            Location = "Remote",
            Status = JobStatus.Open,
            SalaryMax = 160000m,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FullName = "Sam Ops",
            Email = "sam@example.com"
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

        var approval = new OfferApproval
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            ApprovedById = "recruiter-1",
            ApprovedByName = "Jane Recruiter",
            ApprovedAt = DateTime.UtcNow,
            Notes = "Approved"
        };
        _context.OfferApprovals.Add(approval);
        await _context.SaveChangesAsync();

        var handler = new GetOfferLetterQueryHandler(_context, _currentUserMock.Object);
        var query = new GetOfferLetterQuery(app.Id);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().NotBeNull();
        result.CandidateName.Should().Be("Sam Ops");
        result.HtmlDocument.Should().Contain("Employment Offer Letter");
    }

    [Fact]
    public async Task GetJobApplications_ReturnsCardDtos()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "FullStack Engineer",
            Department = "Eng",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var stage = PipelineStage.Create("Applied", 1);
        _context.PipelineStages.Add(stage);

        for (int i = 1; i <= 3; i++)
        {
            var candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = $"Candidate {i}",
                Email = $"cand{i}@example.com"
            };
            _context.Candidates.Add(candidate);

            var app = new DomainApplication
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                CandidateId = candidate.Id,
                CurrentStageId = stage.Id
            };
            _context.Applications.Add(app);
        }
        await _context.SaveChangesAsync();

        var handler = new GetJobApplicationsQueryHandler(_context, _currentUserMock.Object);
        var cards = await handler.Handle(new GetJobApplicationsQuery(job.Id), CancellationToken.None);

        cards.Should().HaveCount(3);
        cards.Should().OnlyContain(c => c.JobId == job.Id);
    }

    [Fact]
    public async Task GetApplicationById_ReturnsApplicationDetail_OrThrowsNotFound()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Security Engineer",
            Department = "Security",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FullName = "Charlie Hacker",
            Email = "charlie@example.com"
        };
        _context.Candidates.Add(candidate);

        var stage = PipelineStage.Create("Applied", 1);
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

        var handler = new GetApplicationByIdQueryHandler(_context, _currentUserMock.Object);
        var detail = await handler.Handle(new GetApplicationByIdQuery(app.Id), CancellationToken.None);

        detail.Should().NotBeNull();
        detail.CandidateName.Should().Be("Charlie Hacker");

        var act = async () => await handler.Handle(new GetApplicationByIdQuery(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task BulkMoveApplications_MovesEligibleApplicationsToRejected()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Senior Dev",
            Department = "Eng",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var appliedStage = PipelineStage.Create("Applied", 1);
        var rejectedStage = PipelineStage.Create("Rejected", 6, StageKind.Rejected);
        _context.PipelineStages.AddRange(appliedStage, rejectedStage);

        var c1 = new Candidate { Id = Guid.NewGuid(), FullName = "Candidate One", Email = "c1@test.com" };
        _context.Candidates.Add(c1);

        var app1 = new DomainApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            CandidateId = c1.Id,
            CurrentStageId = appliedStage.Id
        };
        _context.Applications.Add(app1);
        await _context.SaveChangesAsync();

        var handler = new BulkMoveApplicationsCommandHandler(_context, _currentUserMock.Object, _notifierMock.Object);
        var command = new BulkMoveApplicationsCommand(
            ApplicationIds: new List<Guid> { app1.Id },
            TargetStageId: rejectedStage.Id,
            Reason: "Position filled"
        );

        var movedCount = await handler.Handle(command, CancellationToken.None);
        movedCount.Should().Be(1);

        var movedApp = await _context.Applications.FindAsync(app1.Id);
        movedApp!.CurrentStageId.Should().Be(rejectedStage.Id);
    }
}
