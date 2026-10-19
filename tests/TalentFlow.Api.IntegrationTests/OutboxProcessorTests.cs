using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.BackgroundWorkers;
using TalentFlow.Infrastructure.Persistence;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class OutboxProcessorTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public OutboxProcessorTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ShouldDispatchEmailsAndMarkSent()
    {
        var dedupeKey = $"test-dedupe-{Guid.NewGuid():N}";
        Guid messageId;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var outboxMsg = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "ApplicationConfirmation",
                ToAddress = "candidate@example.com",
                Subject = "Welcome to TalentFlow",
                Body = "<p>Your application was received</p>",
                DedupeKey = dedupeKey,
                Status = OutboxMessageStatus.Pending,
                Attempts = 0,
                NextAttemptAt = DateTime.UtcNow.AddMinutes(-1),
                CreatedAt = DateTime.UtcNow
            };
            context.OutboxMessages.Add(outboxMsg);
            await context.SaveChangesAsync();
            messageId = outboxMsg.Id;
        }

        // Run OutboxProcessor
        var processor = new OutboxProcessor(_factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxProcessor>.Instance);
        await processor.ProcessPendingMessagesAsync();

        // Verify message transitioned to Sent
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var updated = await context.OutboxMessages.FindAsync(messageId);
            updated.Should().NotBeNull();
            updated!.Status.Should().Be(OutboxMessageStatus.Sent);
            updated.SentAt.Should().NotBeNull();
            updated.LockedUntil.Should().BeNull();
        }
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ShouldReclaimStuckProcessingMessages()
    {
        var dedupeKey = $"stuck-dedupe-{Guid.NewGuid():N}";
        Guid messageId;

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stuckMsg = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = "StageChangeNotification",
                ToAddress = "candidate@example.com",
                Subject = "Stage Update",
                Body = "<p>Updated</p>",
                DedupeKey = dedupeKey,
                Status = OutboxMessageStatus.Processing,
                LockedUntil = DateTime.UtcNow.AddMinutes(-10), // Expired lock
                Attempts = 1,
                CreatedAt = DateTime.UtcNow.AddMinutes(-15)
            };
            context.OutboxMessages.Add(stuckMsg);
            await context.SaveChangesAsync();
            messageId = stuckMsg.Id;
        }

        // Run processor - should reclaim and then process to Sent
        var processor = new OutboxProcessor(_factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxProcessor>.Instance);
        await processor.ProcessPendingMessagesAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var processed = await context.OutboxMessages.FindAsync(messageId);
            processed.Should().NotBeNull();
            processed!.Status.Should().Be(OutboxMessageStatus.Sent, "stuck message should be reclaimed and sent");
        }
    }
}
