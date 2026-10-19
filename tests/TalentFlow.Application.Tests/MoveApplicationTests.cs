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

public class MoveApplicationTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<IPipelineNotifier> _notifierMock;
    private readonly MoveApplicationCommandHandler _handler;

    public MoveApplicationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(u => u.UserId).Returns("recruiter-1");
        _currentUserMock.Setup(u => u.Email).Returns("recruiter@talentflow.local");
        _currentUserMock.Setup(u => u.Role).Returns(Roles.Recruiter);
        _currentUserMock.Setup(u => u.FullName).Returns("Jane Recruiter");

        _notifierMock = new Mock<IPipelineNotifier>();

        _handler = new MoveApplicationCommandHandler(_context, _currentUserMock.Object, _notifierMock.Object);
    }

    private async Task<(DomainApplication app, PipelineStage applied, PipelineStage screening, PipelineStage interview, PipelineStage offer, PipelineStage hired)> SeedWorkflowAsync()
    {
        var applied = PipelineStage.Create("Applied", 1);
        var screening = PipelineStage.Create("Screening", 2);
        var interview = PipelineStage.Create("Interview", 3);
        var offer = PipelineStage.Create("Offer", 4);
        var hired = PipelineStage.Create("Hired", 5, StageKind.Hired, isTerminal: true);
        var rejected = PipelineStage.Create("Rejected", 6, StageKind.Rejected);

        _context.PipelineStages.AddRange(applied, screening, interview, offer, hired, rejected);

        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Senior C# Engineer",
            Department = "Engineering",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-1"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FullName = "John Doe",
            Email = "john@example.com"
        };
        _context.Candidates.Add(candidate);

        var app = new DomainApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            CandidateId = candidate.Id,
            CurrentStageId = applied.Id,
            RowVersion = Guid.NewGuid().ToByteArray()
        };
        _context.Applications.Add(app);

        await _context.SaveChangesAsync();
        return (app, applied, screening, interview, offer, hired);
    }

    [Fact]
    public async Task MoveApplication_From_Applied_To_Screening_ShouldSucceed_And_QueueOutbox()
    {
        var (app, applied, screening, _, _, _) = await SeedWorkflowAsync();

        var command = new MoveApplicationCommand(
            app.Id,
            screening.Id,
            "Cleared initial resume screen",
            Convert.ToBase64String(app.RowVersion)
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.StageName.Should().Be("Screening");

        // Verify transition record created
        var transition = await _context.StageTransitions.FirstOrDefaultAsync(t => t.ApplicationId == app.Id);
        transition.Should().NotBeNull();
        transition!.FromStageId.Should().Be(applied.Id);
        transition.ToStageId.Should().Be(screening.Id);

        // Verify outbox message queued
        var outbox = await _context.OutboxMessages.FirstOrDefaultAsync(m => m.Type == "StageChangeNotification");
        outbox.Should().NotBeNull();
        outbox!.Status.Should().Be(OutboxMessageStatus.Pending);

        // Verify notifier called
        _notifierMock.Verify(n => n.NotifyApplicationMoved(
            app.JobId, app.Id, applied.Id, screening.Id, "John Doe", "Screening"), Times.Once);
    }

    [Fact]
    public async Task MoveApplication_With_Stale_ConcurrencyToken_ShouldThrow_ConflictException()
    {
        var (app, _, screening, _, _, _) = await SeedWorkflowAsync();

        var staleToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()); // Different token

        var command = new MoveApplicationCommand(
            app.Id,
            screening.Id,
            "Concurrent move attempt",
            staleToken
        );

        var act = async () => await _handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().WithMessage("*modified by another user*");
    }

    [Fact]
    public async Task MoveApplication_Skipping_To_Offer_Without_AllowSkip_ShouldThrow_ConflictException()
    {
        var (app, _, _, _, offer, _) = await SeedWorkflowAsync();

        var command = new MoveApplicationCommand(
            app.Id,
            offer.Id,
            "Direct offer",
            Convert.ToBase64String(app.RowVersion),
            AllowSkip: false
        );

        var act = async () => await _handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().WithMessage("*Invalid stage transition*");
    }

    [Fact]
    public async Task MoveApplication_To_Offer_Without_Feedback_ShouldThrow_ConflictException()
    {
        var (app, _, _, interview, offer, _) = await SeedWorkflowAsync();

        // Move to interview first
        app.CurrentStageId = interview.Id;
        await _context.SaveChangesAsync();

        var command = new MoveApplicationCommand(
            app.Id,
            offer.Id,
            "Moving to offer",
            Convert.ToBase64String(app.RowVersion)
        );

        var act = async () => await _handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().WithMessage("*interview feedback*");
    }

    [Fact]
    public async Task MoveApplication_To_Hired_Without_OfferApproval_ShouldThrow_ConflictException()
    {
        var (app, _, _, _, offer, hired) = await SeedWorkflowAsync();

        app.CurrentStageId = offer.Id;
        await _context.SaveChangesAsync();

        var command = new MoveApplicationCommand(
            app.Id,
            hired.Id,
            "Hiring candidate",
            Convert.ToBase64String(app.RowVersion)
        );

        var act = async () => await _handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().WithMessage("*offer approval*");
    }
}
