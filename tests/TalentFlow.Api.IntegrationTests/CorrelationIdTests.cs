using FluentAssertions;
using System.Net;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class CorrelationIdTests : IClassFixture<TestWebApplicationFactory>
{
    private const string CorrelationIdHeader = "X-Correlation-ID";
    private readonly TestWebApplicationFactory _factory;

    public CorrelationIdTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Request_WithoutCorrelationId_ReturnsGeneratedGuidCorrelationIdHeader()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains(CorrelationIdHeader).Should().BeTrue("Response must contain X-Correlation-ID header");

        var headerValues = response.Headers.GetValues(CorrelationIdHeader);
        var correlationId = headerValues.Should().ContainSingle().Subject;

        Guid.TryParse(correlationId, out _).Should().BeTrue("Auto-generated correlation ID should be a valid GUID");
    }

    [Fact]
    public async Task Request_WithCustomCorrelationId_EchoesSameCorrelationIdInResponseHeader()
    {
        // Arrange
        var client = _factory.CreateClient();
        const string expectedCorrelationId = "test-corr-id-12345-abcde";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdHeader, expectedCorrelationId);

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains(CorrelationIdHeader).Should().BeTrue("Response must contain X-Correlation-ID header");

        var headerValues = response.Headers.GetValues(CorrelationIdHeader);
        var correlationId = headerValues.Should().ContainSingle().Subject;

        correlationId.Should().Be(expectedCorrelationId, "Response correlation ID must match request correlation ID");
    }
}
