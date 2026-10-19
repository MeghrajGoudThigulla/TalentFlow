using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentFlow.Api.Controllers;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Features.Applications;
using TalentFlow.Application.Features.Jobs.Commands;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using Xunit;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Api.IntegrationTests;

public class QueryPagingAndValidationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public QueryPagingAndValidationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private record PagedListResponse<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount, int TotalPages);

    [Fact]
    public async Task GetJobs_ClampsPageSize_WhenExceeds100()
    {
        var client = _factory.CreateAuthenticatedClient("Recruiter", "recruiter-1", "recruiter@example.com", "Recruiter");

        var response = await client.GetAsync("/api/jobs?pageSize=250&page=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedListResponse<object>>();
        paged.Should().NotBeNull();
        paged!.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task GetJobs_ClampsPageSize_WhenLessThanOne()
    {
        var client = _factory.CreateAuthenticatedClient("Recruiter", "recruiter-1", "recruiter@example.com", "Recruiter");

        var response = await client.GetAsync("/api/jobs?pageSize=-10&page=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await response.Content.ReadFromJsonAsync<PagedListResponse<object>>();
        paged.Should().NotBeNull();
        paged!.PageSize.Should().Be(1);
    }

    [Fact]
    public async Task CreateJob_WithMissingFields_Returns400BadRequest()
    {
        var client = _factory.CreateAuthenticatedClient("Recruiter", "recruiter-1", "recruiter@example.com", "Recruiter");

        var command = new CreateJobCommand(
            Title: "",
            Description: "",
            Department: "",
            Location: "",
            EmploymentType: "FullTime",
            SalaryMin: null,
            SalaryMax: null,
            IsSalaryDisclosed: false,
            Openings: 0,
            HiringManagerId: null
        );

        var response = await client.PostAsJsonAsync("/api/jobs", command);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateJob_WithInvalidSalaryRange_Returns400BadRequest()
    {
        var hmId = "hm-validation-user";
        var client = _factory.CreateAuthenticatedClient("HiringManager", hmId, "hm@example.com", "Hiring Manager");

        var jobId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var job = new Job
            {
                Id = jobId,
                Title = "Backend Engineer",
                Department = "Engineering",
                Location = "Remote",
                Status = JobStatus.Open,
                HiringManagerId = hmId,
                CreatedById = "admin"
            };
            context.Jobs.Add(job);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var command = new UpdateJobCommand(
            Id: jobId,
            Title: "Backend Engineer",
            Description: "Updated description",
            Department: "Engineering",
            Location: "Remote",
            EmploymentType: "FullTime",
            SalaryMin: 200000m,
            SalaryMax: 100000m, // Invalid: Max < Min
            IsSalaryDisclosed: true,
            Openings: 2,
            HiringManagerId: hmId
        );

        var response = await client.PutAsJsonAsync($"/api/jobs/{jobId}", command);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddNote_WithEmptyContent_Returns400BadRequest()
    {
        var client = _factory.CreateAuthenticatedClient("Recruiter", "recruiter-1", "recruiter@example.com", "Recruiter");

        Guid appId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var stage = PipelineStage.Create("Screening", 1);
            context.PipelineStages.Add(stage);

            var job = new Job
            {
                Id = Guid.NewGuid(),
                Title = "Product Manager",
                Department = "Product",
                Location = "Hybrid",
                Status = JobStatus.Open,
                HiringManagerId = "hm-1",
                CreatedById = "admin"
            };
            context.Jobs.Add(job);

            var candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = "Bob Vance",
                Email = "bob.vance@example.com"
            };
            context.Candidates.Add(candidate);

            var app = new DomainApplication
            {
                Id = appId,
                JobId = job.Id,
                CandidateId = candidate.Id,
                CurrentStageId = stage.Id
            };
            context.Applications.Add(app);
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var response = await client.PostAsJsonAsync($"/api/applications/{appId}/notes", new AddNoteRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
