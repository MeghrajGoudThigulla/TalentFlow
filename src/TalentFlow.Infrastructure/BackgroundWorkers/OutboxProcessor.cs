using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Persistence;

namespace TalentFlow.Infrastructure.BackgroundWorkers;

public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;
    private static readonly TimeSpan[] BackoffDelays = new[]
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2)
    };

    public OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessor background worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in OutboxProcessor iteration.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("OutboxProcessor background worker stopping.");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var now = DateTime.UtcNow;

        // Reclaim stuck processing messages older than 5 minutes
        var stuckThreshold = now.AddMinutes(-5);
        var stuckMessages = await context.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.Processing && m.LockedUntil < now)
            .ToListAsync(cancellationToken);

        foreach (var stuck in stuckMessages)
        {
            stuck.Status = OutboxMessageStatus.Pending;
            stuck.LockedUntil = null;
        }

        if (stuckMessages.Any())
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        // Fetch up to 20 pending messages where NextAttemptAt <= now
        var pending = await context.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.Pending && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (!pending.Any())
        {
            return;
        }

        // Mark as Processing and lock for 5 minutes
        foreach (var msg in pending)
        {
            msg.Status = OutboxMessageStatus.Processing;
            msg.LockedUntil = now.AddMinutes(5);
        }
        await context.SaveChangesAsync(cancellationToken);

        // Process each message
        foreach (var msg in pending)
        {
            try
            {
                await emailSender.SendEmailAsync(msg.ToAddress, msg.Subject, msg.Body, cancellationToken);

                msg.Status = OutboxMessageStatus.Sent;
                msg.SentAt = DateTime.UtcNow;
                msg.LastError = null;
                msg.LockedUntil = null;

                _logger.LogInformation("Successfully dispatched OutboxMessage {Id} to {To}", msg.Id, msg.ToAddress);
            }
            catch (Exception ex)
            {
                msg.Attempts++;
                msg.LastError = ex.Message;
                msg.LockedUntil = null;

                if (msg.Attempts >= 5)
                {
                    msg.Status = OutboxMessageStatus.Failed;
                    _logger.LogError(ex, "OutboxMessage {Id} exceeded max attempts (5) and was marked Failed.", msg.Id);
                }
                else
                {
                    msg.Status = OutboxMessageStatus.Pending;
                    var delay = BackoffDelays[Math.Min(msg.Attempts - 1, BackoffDelays.Length - 1)];
                    msg.NextAttemptAt = DateTime.UtcNow.Add(delay);
                    _logger.LogWarning(ex, "Failed to send OutboxMessage {Id}. Attempt {Attempt}/5. Retrying in {Delay}.", msg.Id, msg.Attempts, delay);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
