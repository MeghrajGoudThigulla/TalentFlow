using Microsoft.Extensions.Logging;
using TalentFlow.Application.Common.Interfaces;

namespace TalentFlow.Infrastructure.Services;

public class EmailSender : IEmailSender
{
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(ILogger<EmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "================ EMAIL DISPATCH ================\n" +
            "TO: {To}\n" +
            "SUBJECT: {Subject}\n" +
            "BODY: {Body}\n" +
            "================================================",
            to, subject, body);

        return Task.CompletedTask;
    }
}

public class DateTimeService : IDateTime
{
    public DateTime UtcNow => DateTime.UtcNow;
}
