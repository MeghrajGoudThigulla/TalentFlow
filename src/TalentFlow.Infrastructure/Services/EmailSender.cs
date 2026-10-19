using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using TalentFlow.Application.Common.Interfaces;

namespace TalentFlow.Infrastructure.Services;

public class EmailSender : IEmailSender
{
    private readonly ILogger<EmailSender> _logger;
    private readonly IConfiguration _configuration;

    public EmailSender(ILogger<EmailSender> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var smtpHost = _configuration["EmailSettings:SmtpHost"];
        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            // Simulated/test mode: sanitize logging to avoid PII/sensitive data leaks (SEC-09)
            _logger.LogInformation("Email simulated. Subject: {Subject}, Recipient: {Recipient}", subject, to);
            return;
        }

        var port = int.TryParse(_configuration["EmailSettings:SmtpPort"], out var p) ? p : 1025;
        var fromEmail = _configuration["EmailSettings:FromEmail"] ?? "noreply@talentflow.local";
        var fromName = _configuration["EmailSettings:FromName"] ?? "TalentFlow ATS";
        var useSsl = bool.TryParse(_configuration["EmailSettings:UseSsl"], out var ssl) && ssl;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var builder = new BodyBuilder
        {
            HtmlBody = body
        };
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var secureOption = useSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;
            await client.ConnectAsync(smtpHost, port, secureOption, cancellationToken);

            var username = _configuration["EmailSettings:Username"];
            var password = _configuration["EmailSettings:Password"];
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                await client.AuthenticateAsync(username, password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Email dispatched successfully via SMTP to {Recipient}. Subject: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via SMTP to {Recipient}. Subject: {Subject}", to, subject);
            throw;
        }
    }
}

public class DateTimeService : IDateTime
{
    public DateTime UtcNow => DateTime.UtcNow;
}
