using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TalentFlow.Infrastructure.Services;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class EmailSenderTests
{
    [Fact]
    public async Task SendEmailAsync_InSimulatedMode_ShouldCompleteSuccessfullyWithoutError()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = ""
            })
            .Build();

        var sender = new EmailSender(NullLogger<EmailSender>.Instance, configuration);

        var act = () => sender.SendEmailAsync("candidate@example.com", "Test Subject", "<p>Hello</p>");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendEmailAsync_WithConfiguredHost_AttemptsSmtpConnection()
    {
        // When configured with a mock non-listening port, it throws connection exception (proving it actively uses MailKit SMTP)
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailSettings:SmtpHost"] = "127.0.0.1",
                ["EmailSettings:SmtpPort"] = "65534"
            })
            .Build();

        var sender = new EmailSender(NullLogger<EmailSender>.Instance, configuration);

        var act = () => sender.SendEmailAsync("candidate@example.com", "Application Received", "Body", new CancellationTokenSource(1000).Token);

        await act.Should().ThrowAsync<Exception>("MailKit SmtpClient attempts real network dispatch when SmtpHost is set");
    }
}
