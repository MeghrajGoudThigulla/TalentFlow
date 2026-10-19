using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Text.Json;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Domain.Enums;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class ApplicationCreatedHubEventTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ApplicationCreatedHubEventTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HubConnection BuildHubConnection(string token)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/pipeline"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }

    [Fact]
    public async Task ApplicationCreatedEvent_ShouldContainJobId()
    {
        // 1. Get an existing Open Job from DB
        Guid jobId;
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var job = await context.Jobs.FirstOrDefaultAsync(j => j.Status == JobStatus.Open && !j.IsDeleted);
            if (job == null)
            {
                job = new TalentFlow.Domain.Entities.Job
                {
                    Id = Guid.NewGuid(),
                    Title = "Hub Test Engineer",
                    Department = "Engineering",
                    Location = "Remote",
                    Status = JobStatus.Open,
                    CreatedById = "admin",
                    HiringManagerId = "admin"
                };
                context.Jobs.Add(job);
                await context.SaveChangesAsync();
            }
            jobId = job.Id;
        }

        // 2. Connect as Recruiter to SignalR PipelineHub
        var token = _factory.GenerateToken(Roles.Recruiter, "recruiter-1", "recruiter@talentflow.local", "Sarah Jenkins");
        await using var hubConnection = BuildHubConnection(token);
        await hubConnection.StartAsync();

        // 3. Join the Job Board
        await hubConnection.InvokeAsync("JoinJobBoard", jobId.ToString());

        var receivedEventTcs = new TaskCompletionSource<JsonElement>();
        hubConnection.On<JsonElement>("ApplicationCreated", element =>
        {
            receivedEventTcs.TrySetResult(element);
        });

        // 4. Submit an application via Public Apply endpoint
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("Jane Candidate"), "fullName");
        form.Add(new StringContent($"jane.{Guid.NewGuid():N}@example.com"), "email");
        form.Add(new StringContent("Excited to apply"), "coverNote");

        // Valid PDF magic bytes: %PDF-
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 };
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "resume", "resume.pdf");

        var response = await client.PostAsync($"/api/public/jobs/{jobId}/apply", form);
        response.EnsureSuccessStatusCode();

        // 5. Await SignalR event and assert JobId is present
        var payload = await receivedEventTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var hasJobId = payload.TryGetProperty("jobId", out var jobIdProp) ||
                       payload.TryGetProperty("JobId", out jobIdProp);

        hasJobId.Should().BeTrue("ApplicationCreated event payload must include jobId (ARCH-06)");
        jobIdProp.GetGuid().Should().Be(jobId);
    }
}
