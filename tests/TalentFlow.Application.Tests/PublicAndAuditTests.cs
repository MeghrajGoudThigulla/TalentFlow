using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Features.Audit;
using TalentFlow.Application.Features.Jobs.Commands;
using TalentFlow.Application.Features.Public;
using TalentFlow.Application.Features.Reports;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Persistence;
using Xunit;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Application.Tests;

public class PublicAndAuditTests
{
    private readonly ApplicationDbContext _context;

    public PublicAndAuditTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetPublicJobs_ReturnsOnlyOpenNonDeletedJobs()
    {
        _context.Jobs.Add(new Job
        {
            Id = Guid.NewGuid(),
            Title = "Open Job 1",
            Department = "Engineering",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "admin",
            PublishedAt = DateTime.UtcNow
        });
        _context.Jobs.Add(new Job
        {
            Id = Guid.NewGuid(),
            Title = "Draft Job",
            Department = "Engineering",
            Location = "Remote",
            Status = JobStatus.Draft,
            CreatedById = "admin"
        });
        _context.Jobs.Add(new Job
        {
            Id = Guid.NewGuid(),
            Title = "Deleted Job",
            Department = "Engineering",
            Location = "Remote",
            Status = JobStatus.Open,
            IsDeleted = true,
            CreatedById = "admin"
        });
        await _context.SaveChangesAsync();

        var handler = new GetPublicJobsQueryHandler(_context);
        var result = await handler.Handle(new GetPublicJobsQuery(null, null, null, 1, 10), CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items.First().Title.Should().Be("Open Job 1");
    }

    [Fact]
    public async Task GetPublicJobById_ReturnsJob_OrThrowsNotFound()
    {
        var openJob = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Staff Frontend Engineer",
            Description = "React & TypeScript",
            Department = "Product",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "admin",
            PublishedAt = DateTime.UtcNow
        };
        _context.Jobs.Add(openJob);
        await _context.SaveChangesAsync();

        var handler = new GetPublicJobByIdQueryHandler(_context);
        var found = await handler.Handle(new GetPublicJobByIdQuery(openJob.Id), CancellationToken.None);

        found.Should().NotBeNull();
        found.Title.Should().Be("Staff Frontend Engineer");

        var act = async () => await handler.Handle(new GetPublicJobByIdQuery(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetAuditLogs_QueriesAndFiltersAuditLogs()
    {
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorId = "user-1",
            ActorEmail = "user1@example.com",
            EntityType = "Job",
            EntityId = "job-101",
            Action = "Create",
            OccurredAt = DateTime.UtcNow
        });
        _context.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorId = "user-2",
            ActorEmail = "user2@example.com",
            EntityType = "Application",
            EntityId = "app-202",
            Action = "Move",
            OccurredAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var handler = new GetAuditLogsQueryHandler(_context);
        var result = await handler.Handle(new GetAuditLogsQuery("Job", null, 1, 10), CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items.First().EntityType.Should().Be("Job");
    }

    [Fact]
    public async Task DeleteJob_SoftDeletesJob_WhenFound()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Deprecated Job",
            Department = "Dept",
            Location = "Remote",
            Status = JobStatus.Draft,
            CreatedById = "admin"
        };
        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        var handler = new DeleteJobCommandHandler(_context);
        var res = await handler.Handle(new DeleteJobCommand(job.Id), CancellationToken.None);

        res.Should().Be(Unit.Value);
        var inDb = await _context.Jobs.FindAsync(job.Id);
        inDb!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task GetTimeToHireReport_ReturnsStatsForHiredApplications()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Engineering Lead",
            Department = "Eng",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "admin"
        };
        _context.Jobs.Add(job);

        var candidate = new Candidate
        {
            Id = Guid.NewGuid(),
            FullName = "Hired Dev",
            Email = "hired@dev.com"
        };
        _context.Candidates.Add(candidate);

        var hiredStage = PipelineStage.Create("Hired", 5, StageKind.Hired, isTerminal: true);
        _context.PipelineStages.Add(hiredStage);

        var app = new DomainApplication
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            CandidateId = candidate.Id,
            CurrentStageId = hiredStage.Id,
            AppliedAt = DateTime.UtcNow.AddDays(-20)
        };
        _context.Applications.Add(app);

        var transition = new StageTransition
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            ToStageId = hiredStage.Id,
            OccurredAt = DateTime.UtcNow
        };
        _context.StageTransitions.Add(transition);
        await _context.SaveChangesAsync();

        var handler = new GetTimeToHireReportQueryHandler(_context);
        var result = await handler.Handle(new GetTimeToHireReportQuery(job.Id), CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result.First().TotalHired.Should().Be(1);
        result.First().AverageDaysToHire.Should().BeGreaterThan(15);
    }
}
