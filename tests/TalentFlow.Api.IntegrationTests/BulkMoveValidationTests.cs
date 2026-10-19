using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Features.Applications;
using TalentFlow.Domain.Enums;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class BulkMoveValidationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BulkMoveValidationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BulkMove_ToNonRejectedStage_ShouldReturn409Conflict()
    {
        Guid hiredStageId;
        Guid testAppId;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var hiredStage = await context.PipelineStages.FirstAsync(s => s.Kind == StageKind.Hired);
            hiredStageId = hiredStage.Id;

            var app = await context.Applications.FirstAsync();
            testAppId = app.Id;
        }

        var client = _factory.CreateAuthenticatedClient(Roles.Recruiter, "recruiter-1", "recruiter@talentflow.local", "Sarah Jenkins");

        // Attempt bulk move to "Hired" stage
        var command = new BulkMoveApplicationsCommand(new List<Guid> { testAppId }, hiredStageId, "Bulk hire attempt");
        var response = await client.PostAsJsonAsync("/api/applications/bulk-move", command);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Bulk move is only permitted to the Rejected stage");
    }

    [Fact]
    public async Task BulkMove_ToRejectedStage_ShouldSucceedForValidApplications()
    {
        Guid rejectedStageId;
        Guid testAppId;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var rejectedStage = await context.PipelineStages.FirstAsync(s => s.Kind == StageKind.Rejected);
            var appliedStage = await context.PipelineStages.FirstAsync(s => s.Name == "Applied");
            rejectedStageId = rejectedStage.Id;

            var job = await context.Jobs.FirstAsync();
            var candidate = new TalentFlow.Domain.Entities.Candidate
            {
                Id = Guid.NewGuid(),
                FullName = "Bulk Test Candidate",
                Email = $"bulk.{Guid.NewGuid():N}@example.com"
            };
            context.Candidates.Add(candidate);

            var newApp = new TalentFlow.Domain.Entities.Application
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                CandidateId = candidate.Id,
                CurrentStageId = appliedStage.Id,
                RowVersion = Guid.NewGuid().ToByteArray()
            };
            context.Applications.Add(newApp);
            await context.SaveChangesAsync();
            testAppId = newApp.Id;
        }

        var client = _factory.CreateAuthenticatedClient(Roles.Recruiter, "recruiter-1", "recruiter@talentflow.local", "Sarah Jenkins");

        var command = new BulkMoveApplicationsCommand(new List<Guid> { testAppId }, rejectedStageId, "Candidate not qualified");
        var response = await client.PostAsJsonAsync("/api/applications/bulk-move", command);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var movedApp = await context.Applications.FindAsync(testAppId);
            movedApp.Should().NotBeNull();
            movedApp!.CurrentStageId.Should().Be(rejectedStageId);
        }
    }
}
