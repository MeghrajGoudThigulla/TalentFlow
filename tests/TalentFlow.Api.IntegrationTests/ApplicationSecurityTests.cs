using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using Xunit;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Api.IntegrationTests;

public class ApplicationSecurityTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApplicationSecurityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HiringManager_B_Gets403_On_HiringManager_A_Application_Notes_And_OfferLetter()
    {
        var hmAId = "hm-user-A";
        var hmBId = "hm-user-B";

        Guid appId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var stage = PipelineStage.Create("Offer", 4);
            context.PipelineStages.Add(stage);

            var job = new Job
            {
                Id = Guid.NewGuid(),
                Title = "Principal Engineer",
                Department = "Infrastructure",
                Location = "Remote",
                Status = JobStatus.Open,
                HiringManagerId = hmAId,
                CreatedById = "admin"
            };
            context.Jobs.Add(job);

            var candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = "Eve Hacker",
                Email = "eve@example.com"
            };
            context.Candidates.Add(candidate);

            var app = new DomainApplication
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                CandidateId = candidate.Id,
                CurrentStageId = stage.Id
            };
            context.Applications.Add(app);

            var approval = new OfferApproval
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                ApprovedById = hmAId,
                ApprovedByName = "HM Alice",
                ApprovedAt = DateTime.UtcNow
            };
            context.OfferApprovals.Add(approval);

            await context.SaveChangesAsync();
            appId = app.Id;
        }

        // Connect as HM_B
        var clientB = _factory.CreateAuthenticatedClient(Roles.HiringManager, hmBId, "hmb@talentflow.local", "HM Bob");

        // 1. HM_B tries to add note to HM_A's application -> 403 Forbidden
        var noteResponse = await clientB.PostAsJsonAsync($"/api/applications/{appId}/notes", new { body = "Unauthorized note" });
        noteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. HM_B tries to get offer letter for HM_A's application -> 403 Forbidden
        var offerResponse = await clientB.GetAsync($"/api/applications/{appId}/offer-letter");
        offerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. HM_A can access both successfully
        var clientA = _factory.CreateAuthenticatedClient(Roles.HiringManager, hmAId, "hma@talentflow.local", "HM Alice");

        var noteA = await clientA.PostAsJsonAsync($"/api/applications/{appId}/notes", new { body = "Legitimate note by HM A" });
        noteA.StatusCode.Should().Be(HttpStatusCode.OK);

        var offerA = await clientA.GetAsync($"/api/applications/{appId}/offer-letter");
        offerA.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OfferLetter_HtmlEncodes_CandidateName_Preventing_StoredXSS()
    {
        var hmAId = "hm-xss-lead";
        Guid appId;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var stage = PipelineStage.Create("Offer", 4);
            context.PipelineStages.Add(stage);

            var job = new Job
            {
                Id = Guid.NewGuid(),
                Title = "Security Engineer",
                Department = "SecOps",
                Location = "Remote",
                Status = JobStatus.Open,
                HiringManagerId = hmAId,
                CreatedById = "admin"
            };
            context.Jobs.Add(job);

            var candidate = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = "<script>alert('XSS')</script>John",
                Email = "xss@example.com"
            };
            context.Candidates.Add(candidate);

            var app = new DomainApplication
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                CandidateId = candidate.Id,
                CurrentStageId = stage.Id
            };
            context.Applications.Add(app);

            var approval = new OfferApproval
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                ApprovedById = hmAId,
                ApprovedByName = "Lead HM",
                ApprovedAt = DateTime.UtcNow
            };
            context.OfferApprovals.Add(approval);

            await context.SaveChangesAsync();
            appId = app.Id;
        }

        var client = _factory.CreateAuthenticatedClient(Roles.HiringManager, hmAId, "lead@talentflow.local", "Lead HM");

        var response = await client.GetAsync($"/api/applications/{appId}/offer-letter?rawHtml=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotContain("<script>alert('XSS')</script>");
        html.Should().Contain("&lt;script&gt;alert(");
    }
}
