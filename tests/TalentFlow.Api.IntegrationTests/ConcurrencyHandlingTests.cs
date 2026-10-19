using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentFlow.Api.Controllers;
using TalentFlow.Api.Middleware;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Domain.Enums;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class ConcurrencyHandlingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ConcurrencyHandlingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConcurrentMove_WithStaleConcurrencyToken_ShouldReturn409Conflict()
    {
        // 1. Get an application in Applied stage and target Screening stage
        Guid applicationId;
        Guid targetStageId;
        string initialConcurrencyToken;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var appliedStage = await context.PipelineStages.FirstAsync(s => s.Name == "Applied");
            var screeningStage = await context.PipelineStages.FirstAsync(s => s.Name == "Screening");
            targetStageId = screeningStage.Id;

            var app = await context.Applications.FirstOrDefaultAsync(a => a.CurrentStageId == appliedStage.Id);
            if (app == null)
            {
                var job = await context.Jobs.FirstAsync();
                var candidate = await context.Candidates.FirstAsync();
                app = new TalentFlow.Domain.Entities.Application
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    CandidateId = candidate.Id,
                    CurrentStageId = appliedStage.Id,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };
                context.Applications.Add(app);
                await context.SaveChangesAsync();
            }

            applicationId = app.Id;
            initialConcurrencyToken = Convert.ToBase64String(app.RowVersion);
        }

        var client = _factory.CreateAuthenticatedClient(Roles.Recruiter, "recruiter-1", "recruiter@talentflow.local", "Sarah Jenkins");

        // 2. User 1 successfully moves the application to Screening
        var moveRequest1 = new MoveApplicationRequest(targetStageId, "Screening candidate", initialConcurrencyToken, AllowSkip: false);
        var response1 = await client.PostAsJsonAsync($"/api/applications/{applicationId}/move", moveRequest1);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. User 2 tries to move the application with the stale initialConcurrencyToken
        var moveRequest2 = new MoveApplicationRequest(targetStageId, "Concurrent move attempt", initialConcurrencyToken, AllowSkip: false);
        var response2 = await client.PostAsJsonAsync($"/api/applications/{applicationId}/move", moveRequest2);

        // 4. Assert HTTP 409 Conflict
        response2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var content = await response2.Content.ReadAsStringAsync();
        content.Should().Contain("modified by another user");
    }

    [Fact]
    public async Task ExceptionHandlingMiddleware_MapsDbUpdateConcurrencyException_To409ConflictProblemDetails()
    {
        // Direct middleware test verifying DATA-02 mapping of DbUpdateConcurrencyException
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new DbUpdateConcurrencyException("Optimistic concurrency violation"),
            NullLogger<ExceptionHandlingMiddleware>.Instance
        );

        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        httpContext.Response.ContentType.Should().Be("application/problem+json");

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var json = await JsonDocument.ParseAsync(httpContext.Response.Body);
        var root = json.RootElement;

        root.GetProperty("status").GetInt32().Should().Be(409);
        root.GetProperty("title").GetString().Should().Be("Concurrency Conflict");
        root.GetProperty("detail").GetString().Should().Contain("The resource was modified by another operation");
    }
}
