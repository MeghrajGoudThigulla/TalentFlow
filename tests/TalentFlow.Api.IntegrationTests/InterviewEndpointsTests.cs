using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TalentFlow.Api.Controllers;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using Xunit;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Api.IntegrationTests;

public class InterviewEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public InterviewEndpointsTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RescheduleInterview_Returns200_WithUpdatedDetails()
    {
        var recruiterId = "recruiter-int-1";
        var client = _factory.CreateAuthenticatedClient(Roles.Recruiter, recruiterId, "recruiter@test.com", "Recruiter");

        Guid interviewId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var job = new Job
            {
                Id = Guid.NewGuid(),
                Title = "Site Reliability Engineer",
                Department = "SRE",
                Location = "Remote",
                Status = JobStatus.Open,
                CreatedById = recruiterId
            };
            context.Jobs.Add(job);

            var candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = "Bob SRE",
                Email = "bobsre@test.com"
            };
            context.Candidates.Add(candidate);

            var stage = PipelineStage.Create("Interview", 2);
            context.PipelineStages.Add(stage);

            var app = new DomainApplication
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                CandidateId = candidate.Id,
                CurrentStageId = stage.Id
            };
            context.Applications.Add(app);

            var interview = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                StartsAt = DateTime.UtcNow.AddDays(2),
                DurationMinutes = 45,
                Type = InterviewType.Video,
                Status = InterviewStatus.Scheduled
            };
            interview.Participants.Add(new InterviewParticipant
            {
                InterviewId = interview.Id,
                InterviewerId = recruiterId,
                InterviewerName = "Recruiter"
            });
            context.Interviews.Add(interview);
            await context.SaveChangesAsync(CancellationToken.None);
            interviewId = interview.Id;
        }

        var newStartsAt = DateTime.UtcNow.AddDays(4);
        var request = new RescheduleInterviewRequest(
            StartsAt: newStartsAt,
            DurationMinutes: 60,
            Type: InterviewType.Onsite,
            LocationOrLink: "Conference Room B"
        );

        var response = await client.PutAsJsonAsync($"/api/interviews/{interviewId}", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<InterviewDto>();
        dto.Should().NotBeNull();
        dto!.DurationMinutes.Should().Be(60);
        dto.Type.Should().Be(InterviewType.Onsite);
        dto.LocationOrLink.Should().Be("Conference Room B");
    }

    [Fact]
    public async Task CancelInterview_Returns204NoContent_AndCancelsInterview()
    {
        var recruiterId = "recruiter-int-2";
        var client = _factory.CreateAuthenticatedClient(Roles.Recruiter, recruiterId, "recruiter2@test.com", "Recruiter");

        Guid interviewId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var job = new Job
            {
                Id = Guid.NewGuid(),
                Title = "Cloud Architect",
                Department = "Cloud",
                Location = "Remote",
                Status = JobStatus.Open,
                CreatedById = recruiterId
            };
            context.Jobs.Add(job);

            var candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = "Alice Cloud",
                Email = "alicecloud@test.com"
            };
            context.Candidates.Add(candidate);

            var stage = PipelineStage.Create("Interview", 2);
            context.PipelineStages.Add(stage);

            var app = new DomainApplication
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                CandidateId = candidate.Id,
                CurrentStageId = stage.Id
            };
            context.Applications.Add(app);

            var interview = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                StartsAt = DateTime.UtcNow.AddDays(1),
                DurationMinutes = 45,
                Type = InterviewType.Video,
                Status = InterviewStatus.Scheduled
            };
            context.Interviews.Add(interview);
            await context.SaveChangesAsync(CancellationToken.None);
            interviewId = interview.Id;
        }

        var response = await client.DeleteAsync($"/api/interviews/{interviewId}?reason=CandidateRequestedWithdrawal");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
