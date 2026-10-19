using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class SignalRHubSecurityTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SignalRHubSecurityTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HubConnection BuildHubConnection(string? token = null)
    {
        return new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/pipeline"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                if (token != null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();
    }

    [Fact]
    public async Task AnonymousConnect_ShouldBeRejectedWithUnauthorized()
    {
        await using var connection = BuildHubConnection(token: null);

        var act = async () => await connection.StartAsync();

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task HiringManager_CannotJoin_UnassignedJobBoard()
    {
        // 1. Seed a job assigned to HM_A
        var hmAId = "hm-user-A";
        var hmBId = "hm-user-B";
        var jobId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            context.Jobs.Add(new Job
            {
                Id = jobId,
                Title = "Staff Cloud Architect",
                Department = "Engineering",
                Location = "Remote",
                Status = JobStatus.Open,
                HiringManagerId = hmAId,
                CreatedById = "admin"
            });
            await context.SaveChangesAsync();
        }

        // 2. Connect as HM_B
        var tokenB = _factory.GenerateToken(Roles.HiringManager, hmBId, "hmb@talentflow.local", "HM Bob");
        await using var connectionB = BuildHubConnection(tokenB);
        await connectionB.StartAsync();

        // 3. Attempt to join HM_A's job board
        var actB = async () => await connectionB.InvokeAsync("JoinJobBoard", jobId.ToString());
        await actB.Should().ThrowAsync<HubException>()
            .WithMessage("*Forbidden*");

        // 4. Verify HM_A CAN join HM_A's job board
        var tokenA = _factory.GenerateToken(Roles.HiringManager, hmAId, "hma@talentflow.local", "HM Alice");
        await using var connectionA = BuildHubConnection(tokenA);
        await connectionA.StartAsync();

        var actA = async () => await connectionA.InvokeAsync("JoinJobBoard", jobId.ToString());
        await actA.Should().NotThrowAsync();
    }
}
