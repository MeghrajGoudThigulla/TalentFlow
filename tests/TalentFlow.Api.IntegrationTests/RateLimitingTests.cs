using FluentAssertions;
using System.Net;
using System.Net.Http.Json;
using TalentFlow.Application.Features.Auth;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class RateLimitingTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public RateLimitingTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SixthLoginAttemptWithinOneMinute_ShouldReturn429TooManyRequests()
    {
        var client = _factory.CreateClient();
        var loginRequest = new LoginCommand("nonexistent@talentflow.local", "WrongPassword123!");

        // First 5 requests should pass rate limiter
        for (int i = 0; i < 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, $"attempt {i + 1} should be permitted by rate limiter");
        }

        // 6th request exceeds limit of 5 per minute
        var sixthResponse = await client.PostAsJsonAsync("/api/auth/login", loginRequest);
        sixthResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
