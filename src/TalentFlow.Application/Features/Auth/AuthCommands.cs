using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using ValidationException = TalentFlow.Application.Common.Exceptions.ValidationException;

namespace TalentFlow.Application.Features.Auth;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(string userId, string email, string fullName, string role);
    string GenerateRefreshToken();
}

public record LoginCommand(string Email, string Password) : IRequest<AuthResponseDto>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponseDto>
{
    private readonly IIdentityService _identityService;
    private readonly IJwtTokenGenerator _jwtGenerator;
    private readonly IApplicationDbContext _context;

    public LoginCommandHandler(
        IIdentityService identityService,
        IJwtTokenGenerator jwtGenerator,
        IApplicationDbContext context)
    {
        _identityService = identityService;
        _jwtGenerator = jwtGenerator;
        _context = context;
    }

    public async Task<AuthResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var (succeeded, userId, fullName, role, error) = await _identityService.ValidateUserCredentialsAsync(
            request.Email.Trim(), request.Password);

        if (!succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                { "Credentials", new[] { error ?? "Invalid email or password." } }
            });
        }

        var (accessToken, expiresAt) = _jwtGenerator.GenerateAccessToken(userId, request.Email.Trim(), fullName, role);
        var rawRefreshToken = _jwtGenerator.GenerateRefreshToken();
        var hashedRefreshToken = HashToken(rawRefreshToken);

        var tokenRecord = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hashedRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        _context.RefreshTokens.Add(tokenRecord);
        await _context.SaveChangesAsync(cancellationToken);

        return new AuthResponseDto(
            accessToken,
            rawRefreshToken,
            expiresAt,
            userId,
            request.Email.Trim(),
            fullName,
            role
        );
    }

    private static string HashToken(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}

public record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResponseDto>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResponseDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IIdentityService _identityService;
    private readonly IJwtTokenGenerator _jwtGenerator;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        IIdentityService identityService,
        IJwtTokenGenerator jwtGenerator)
    {
        _context = context;
        _identityService = identityService;
        _jwtGenerator = jwtGenerator;
    }

    public async Task<AuthResponseDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var hashedToken = HashToken(request.RefreshToken);

        var existingToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hashedToken, cancellationToken);

        if (existingToken == null || !existingToken.IsActive)
        {
            // If revoked token is reused, family reuse detection revokes all tokens for that user!
            if (existingToken != null && existingToken.IsRevoked)
            {
                var allUserTokens = await _context.RefreshTokens
                    .Where(t => t.UserId == existingToken.UserId && t.RevokedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var t in allUserTokens)
                {
                    t.RevokedAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync(cancellationToken);
            }
            throw new ForbiddenException("Invalid or expired refresh token.");
        }

        var user = await _identityService.GetUserByIdAsync(existingToken.UserId)
            ?? throw new ForbiddenException("User not found.");

        // Rotate refresh token
        var newRawRefreshToken = _jwtGenerator.GenerateRefreshToken();
        var newHashed = HashToken(newRawRefreshToken);

        existingToken.RevokedAt = DateTime.UtcNow;
        existingToken.ReplacedByTokenHash = newHashed;

        var newToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = newHashed,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        _context.RefreshTokens.Add(newToken);
        await _context.SaveChangesAsync(cancellationToken);

        var (accessToken, expiresAt) = _jwtGenerator.GenerateAccessToken(user.Id, user.Email, user.FullName, user.Role);

        return new AuthResponseDto(
            accessToken,
            newRawRefreshToken,
            expiresAt,
            user.Id,
            user.Email,
            user.FullName,
            user.Role
        );
    }

    private static string HashToken(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}

public record LogoutCommand(string RefreshToken) : IRequest<Unit>;

public class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IApplicationDbContext _context;

    public LogoutCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(request.RefreshToken);
        var hashed = Convert.ToBase64String(SHA256.HashData(bytes));

        var token = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hashed, cancellationToken);
        if (token != null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}

public record CreateUserCommand(string Email, string Password, string FullName, string Role) : IRequest<UserDto>;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Role).NotEmpty();
    }
}

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IIdentityService _identityService;

    public CreateUserCommandHandler(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var (succeeded, userId, error) = await _identityService.CreateUserAsync(
            request.Email.Trim(), request.Password, request.FullName.Trim(), request.Role);

        if (!succeeded)
        {
            throw new ConflictException(error ?? "Failed to create user.");
        }

        return new UserDto(userId, request.Email.Trim(), request.FullName.Trim(), request.Role, true, DateTime.UtcNow);
    }
}

public record GetUsersQuery : IRequest<List<UserDto>>;

public class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, List<UserDto>>
{
    private readonly IIdentityService _identityService;

    public GetUsersQueryHandler(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<List<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        return await _identityService.GetUsersAsync();
    }
}

public record UpdateUserRoleCommand(string UserId, string NewRole) : IRequest<bool>;

public class UpdateUserRoleCommandValidator : AbstractValidator<UpdateUserRoleCommand>
{
    public UpdateUserRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewRole).NotEmpty();
    }
}

public class UpdateUserRoleCommandHandler : IRequestHandler<UpdateUserRoleCommand, bool>
{
    private readonly IIdentityService _identityService;

    public UpdateUserRoleCommandHandler(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<bool> Handle(UpdateUserRoleCommand request, CancellationToken cancellationToken)
    {
        return await _identityService.UpdateUserRoleAsync(request.UserId, request.NewRole);
    }
}
