using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Features.Jobs.Commands;
using TalentFlow.Application.Features.Jobs.Queries;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Persistence;
using Xunit;

namespace TalentFlow.Application.Tests;

public class JobCommandHandlerTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<ICurrentUserService> _currentUserMock;

    public JobCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        _currentUserMock = new Mock<ICurrentUserService>();
        _currentUserMock.Setup(u => u.UserId).Returns("recruiter-42");
        _currentUserMock.Setup(u => u.Role).Returns(Roles.Recruiter);
        _currentUserMock.Setup(u => u.FullName).Returns("Recruiter Name");
    }

    [Fact]
    public async Task CreateJob_SavesJobInDraftStatus_AndAssignsCreatedBy()
    {
        var handler = new CreateJobCommandHandler(_context, _currentUserMock.Object);
        var command = new CreateJobCommand(
            Title: "Lead .NET Architect",
            Description: "Designing resilient cloud native systems",
            Department: "Architecture",
            Location: "Remote",
            EmploymentType: "FullTime",
            SalaryMin: 180000m,
            SalaryMax: 220000m,
            IsSalaryDisclosed: true,
            Openings: 1,
            HiringManagerId: "hm-99"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.Title.Should().Be("Lead .NET Architect");
        result.Department.Should().Be("Architecture");
        result.Status.Should().Be(JobStatus.Draft);
        result.HiringManagerId.Should().Be("hm-99");

        var dbJob = await _context.Jobs.FindAsync(result.Id);
        dbJob.Should().NotBeNull();
        dbJob!.CreatedById.Should().Be("recruiter-42");
    }

    [Fact]
    public async Task UpdateJob_UpdatesDetails_WhenJobExists()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Old Title",
            Description = "Old Desc",
            Department = "Engineering",
            Location = "Onsite",
            EmploymentType = "FullTime",
            Status = JobStatus.Draft,
            CreatedById = "recruiter-42"
        };
        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        var handler = new UpdateJobCommandHandler(_context, _currentUserMock.Object);
        var command = new UpdateJobCommand(
            Id: job.Id,
            Title: "New Title",
            Description: "New Desc",
            Department: "Product",
            Location: "Remote",
            EmploymentType: "Contract",
            SalaryMin: 120000m,
            SalaryMax: 150000m,
            IsSalaryDisclosed: false,
            Openings: 3,
            HiringManagerId: "hm-1"
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.Title.Should().Be("New Title");
        result.Department.Should().Be("Product");
        result.Location.Should().Be("Remote");
        result.Openings.Should().Be(3);
    }

    [Fact]
    public async Task UpdateJob_ThrowsNotFoundException_WhenJobDoesNotExist()
    {
        var handler = new UpdateJobCommandHandler(_context, _currentUserMock.Object);
        var command = new UpdateJobCommand(
            Id: Guid.NewGuid(),
            Title: "Nonexistent",
            Description: "Desc",
            Department: "Dept",
            Location: "Remote",
            EmploymentType: "FullTime",
            SalaryMin: null,
            SalaryMax: null,
            IsSalaryDisclosed: false,
            Openings: 1,
            HiringManagerId: null
        );

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task PublishJob_SetsStatusToOpen_WhenInDraftStatus()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "DevOps Engineer",
            Description = "Kubernetes & CI/CD",
            Department = "Infrastructure",
            Location = "Remote",
            Status = JobStatus.Draft,
            CreatedById = "recruiter-42"
        };
        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        var handler = new PublishJobCommandHandler(_context);
        var result = await handler.Handle(new PublishJobCommand(job.Id), CancellationToken.None);

        result.Status.Should().Be(JobStatus.Open);
        var dbJob = await _context.Jobs.FindAsync(job.Id);
        dbJob!.Status.Should().Be(JobStatus.Open);
    }

    [Fact]
    public async Task PublishJob_ThrowsInvalidOperationException_WhenClosed()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "DevOps Engineer",
            Description = "Kubernetes & CI/CD",
            Department = "Infrastructure",
            Location = "Remote",
            Status = JobStatus.Closed,
            CreatedById = "recruiter-42"
        };
        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        var handler = new PublishJobCommandHandler(_context);
        var act = async () => await handler.Handle(new PublishJobCommand(job.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CloseJob_SetsStatusToClosed_WhenOpen()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "QA Lead",
            Description = "Automated testing",
            Department = "Quality",
            Location = "Remote",
            Status = JobStatus.Open,
            CreatedById = "recruiter-42"
        };
        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        var handler = new CloseJobCommandHandler(_context);
        var result = await handler.Handle(new CloseJobCommand(job.Id), CancellationToken.None);

        result.Status.Should().Be(JobStatus.Closed);
        var dbJob = await _context.Jobs.FindAsync(job.Id);
        dbJob!.Status.Should().Be(JobStatus.Closed);
    }

    [Fact]
    public async Task GetJobsQuery_FiltersAndPagesResults()
    {
        for (int i = 1; i <= 5; i++)
        {
            _context.Jobs.Add(new Job
            {
                Id = Guid.NewGuid(),
                Title = i % 2 == 0 ? $"Backend Engineer {i}" : $"Frontend Engineer {i}",
                Description = "Engineering position",
                Department = i % 2 == 0 ? "Backend" : "Frontend",
                Location = "Remote",
                Status = JobStatus.Open,
                CreatedById = "recruiter-42"
            });
        }
        await _context.SaveChangesAsync();

        var handler = new GetJobsQueryHandler(_context, _currentUserMock.Object);

        var query = new GetJobsQuery(
            Search: "Backend",
            Department: "Backend",
            Status: JobStatus.Open,
            Location: null,
            PageNumber: 1,
            PageSize: 10
        );

        var pagedList = await handler.Handle(query, CancellationToken.None);

        pagedList.Items.Should().HaveCount(2);
        pagedList.TotalCount.Should().Be(2);
        pagedList.Items.Should().OnlyContain(j => j.Department == "Backend");
    }

    [Fact]
    public async Task GetJobByIdQuery_ReturnsJob_OrThrowsNotFound()
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = "Security Engineer",
            Description = "SecOps",
            Department = "Security",
            Location = "Hybrid",
            Status = JobStatus.Open,
            CreatedById = "recruiter-42"
        };
        _context.Jobs.Add(job);
        await _context.SaveChangesAsync();

        var handler = new GetJobByIdQueryHandler(_context, _currentUserMock.Object);

        var found = await handler.Handle(new GetJobByIdQuery(job.Id), CancellationToken.None);
        found.Should().NotBeNull();
        found.Title.Should().Be("Security Engineer");

        var notFoundAct = async () => await handler.Handle(new GetJobByIdQuery(Guid.NewGuid()), CancellationToken.None);
        await notFoundAct.Should().ThrowAsync<NotFoundException>();
    }
}
