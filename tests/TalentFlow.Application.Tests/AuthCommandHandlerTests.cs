using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Cryptography;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Application.Features.Auth;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Persistence;
using Xunit;

namespace TalentFlow.Application.Tests;

public class AuthCommandHandlerTests
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IIdentityService> _identityServiceMock;
    private readonly Mock<IJwtTokenGenerator> _jwtGeneratorMock;

    public AuthCommandHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _identityServiceMock = new Mock<IIdentityService>();
        _jwtGeneratorMock = new Mock<IJwtTokenGenerator>();
    }

    [Fact]
    public async Task Login_ReturnsTokens_WhenCredentialsAreValid()
    {
        _identityServiceMock.Setup(i => i.ValidateUserCredentialsAsync("user@example.com", "Password123!"))
            .ReturnsAsync((true, "user-1", "User One", Roles.Recruiter, null));

        _jwtGeneratorMock.Setup(j => j.GenerateAccessToken("user-1", "user@example.com", "User One", Roles.Recruiter))
            .Returns(("access-token-123", DateTime.UtcNow.AddMinutes(15)));

        _jwtGeneratorMock.Setup(j => j.GenerateRefreshToken())
            .Returns("refresh-token-456");

        var handler = new LoginCommandHandler(_identityServiceMock.Object, _jwtGeneratorMock.Object, _context);
        var command = new LoginCommand("user@example.com", "Password123!");

        var result = await handler.Handle(command, CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("access-token-123");
        result.RefreshToken.Should().Be("refresh-token-456");
        result.Email.Should().Be("user@example.com");

        var tokenInDb = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.UserId == "user-1");
        tokenInDb.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_ThrowsValidationException_WhenCredentialsAreInvalid()
    {
        _identityServiceMock.Setup(i => i.ValidateUserCredentialsAsync("bad@example.com", "WrongPassword"))
            .ReturnsAsync((false, "", "", "", "Invalid credentials"));

        var handler = new LoginCommandHandler(_identityServiceMock.Object, _jwtGeneratorMock.Object, _context);
        var command = new LoginCommand("bad@example.com", "WrongPassword");

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<Common.Exceptions.ValidationException>();
    }

    [Fact]
    public async Task RefreshToken_ReturnsNewTokens_AndRotatesOldToken()
    {
        var rawToken = "valid-refresh-token";
        var rawTokenHash = Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

        var refreshTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            TokenHash = rawTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync();

        _identityServiceMock.Setup(i => i.GetUserByIdAsync("user-1"))
            .ReturnsAsync(new UserDto("user-1", "user@example.com", "User One", Roles.Recruiter, true, DateTime.UtcNow));

        _jwtGeneratorMock.Setup(j => j.GenerateAccessToken("user-1", "user@example.com", "User One", Roles.Recruiter))
            .Returns(("new-access-token", DateTime.UtcNow.AddMinutes(15)));

        _jwtGeneratorMock.Setup(j => j.GenerateRefreshToken())
            .Returns("new-refresh-token");

        var handler = new RefreshTokenCommandHandler(_context, _identityServiceMock.Object, _jwtGeneratorMock.Object);
        var result = await handler.Handle(new RefreshTokenCommand(rawToken), CancellationToken.None);

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("new-access-token");
        result.RefreshToken.Should().Be("new-refresh-token");

        // Verify old token was revoked/replaced
        var oldToken = await _context.RefreshTokens.FindAsync(refreshTokenEntity.Id);
        oldToken!.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var rawToken = "logout-refresh-token";
        var rawTokenHash = Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            TokenHash = rawTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync();

        var handler = new LogoutCommandHandler(_context);
        await handler.Handle(new LogoutCommand(rawToken), CancellationToken.None);

        var updated = await _context.RefreshTokens.FindAsync(token.Id);
        updated!.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateUser_CallsIdentityService_AndReturnsUserDto()
    {
        _identityServiceMock.Setup(i => i.CreateUserAsync("new@talentflow.local", "Pass123!", "New User", Roles.Recruiter))
            .ReturnsAsync((true, "new-id", null));

        var handler = new CreateUserCommandHandler(_identityServiceMock.Object);
        var result = await handler.Handle(new CreateUserCommand("new@talentflow.local", "Pass123!", "New User", Roles.Recruiter), CancellationToken.None);

        result.Should().NotBeNull();
        result.Id.Should().Be("new-id");
        result.FullName.Should().Be("New User");
    }

    [Fact]
    public async Task GetUsers_ReturnsUserList()
    {
        _identityServiceMock.Setup(i => i.GetUsersAsync())
            .ReturnsAsync(new List<UserDto>
            {
                new("u1", "u1@test.com", "User 1", Roles.Admin, true, DateTime.UtcNow)
            });

        var handler = new GetUsersQueryHandler(_identityServiceMock.Object);
        var list = await handler.Handle(new GetUsersQuery(), CancellationToken.None);

        list.Should().HaveCount(1);
    }

    [Fact]
    public async Task UpdateUserRole_DelegatesToIdentityService()
    {
        _identityServiceMock.Setup(i => i.UpdateUserRoleAsync("u1", Roles.Admin))
            .ReturnsAsync(true);

        var handler = new UpdateUserRoleCommandHandler(_identityServiceMock.Object);
        var success = await handler.Handle(new UpdateUserRoleCommand("u1", Roles.Admin), CancellationToken.None);

        success.Should().BeTrue();
    }
}
