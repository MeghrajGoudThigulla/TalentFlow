using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Identity;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class IdentityLockoutTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public IdentityLockoutTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FiveFailedLogins_ShouldLockOutAccount()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var identityService = scope.ServiceProvider.GetRequiredService<IIdentityService>();

        // Create a dedicated user for lockout testing
        var testEmail = $"lockout-user-{Guid.NewGuid():N}@talentflow.local";
        var user = new ApplicationUser
        {
            UserName = testEmail,
            Email = testEmail,
            FullName = "Lockout Test User",
            Role = Roles.Recruiter,
            IsActive = true,
            LockoutEnabled = true
        };

        var createResult = await userManager.CreateAsync(user, "CorrectPassword123!");
        createResult.Succeeded.Should().BeTrue();

        // 4 failed attempts should result in "Invalid credentials."
        for (int i = 0; i < 4; i++)
        {
            var (succeeded, _, _, _, error) = await identityService.ValidateUserCredentialsAsync(testEmail, "WrongPassword!");
            succeeded.Should().BeFalse();
            error.Should().Be("Invalid credentials.");
        }

        // 5th failed attempt triggers account lockout
        var (succeeded5, _, _, _, error5) = await identityService.ValidateUserCredentialsAsync(testEmail, "WrongPassword!");
        succeeded5.Should().BeFalse();
        error5.Should().Contain("locked out");

        // 6th attempt with CORRECT password must still be rejected due to lockout
        var (succeeded6, _, _, _, error6) = await identityService.ValidateUserCredentialsAsync(testEmail, "CorrectPassword123!");
        succeeded6.Should().BeFalse();
        error6.Should().Contain("locked out");

        // Verify with UserManager directly
        var freshUser = await userManager.FindByEmailAsync(testEmail);
        freshUser.Should().NotBeNull();
        var isLocked = await userManager.IsLockedOutAsync(freshUser!);
        isLocked.Should().BeTrue();
    }
}
