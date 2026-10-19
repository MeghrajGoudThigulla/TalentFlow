using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Interfaces;

namespace TalentFlow.Infrastructure.Identity;

public class IdentityService : IIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public IdentityService(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<(bool Succeeded, string UserId, string? Error)> CreateUserAsync(string email, string password, string fullName, string role)
    {
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            return (false, string.Empty, "A user with this email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            return (false, string.Empty, errors);
        }

        if (!await _roleManager.RoleExistsAsync(role))
        {
            await _roleManager.CreateAsync(new IdentityRole(role));
        }

        await _userManager.AddToRoleAsync(user, role);

        return (true, user.Id, null);
    }

    public async Task<(bool Succeeded, string UserId, string FullName, string Role, string? Error)> ValidateUserCredentialsAsync(string email, string password)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || !user.IsActive)
        {
            return (false, string.Empty, string.Empty, string.Empty, "Invalid email or user account is inactive.");
        }

        var passwordValid = await _userManager.CheckPasswordAsync(user, password);
        if (!passwordValid)
        {
            return (false, string.Empty, string.Empty, string.Empty, "Invalid credentials.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? user.Role;

        return (true, user.Id, user.FullName, role, null);
    }

    public async Task<List<UserDto>> GetUsersAsync()
    {
        return await _userManager.Users
            .OrderBy(u => u.FullName)
            .Select(u => new UserDto(u.Id, u.Email!, u.FullName, u.Role, u.IsActive, u.CreatedAt))
            .ToListAsync();
    }

    public async Task<bool> UpdateUserRoleAsync(string userId, string newRole)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);

        if (!await _roleManager.RoleExistsAsync(newRole))
        {
            await _roleManager.CreateAsync(new IdentityRole(newRole));
        }

        await _userManager.AddToRoleAsync(user, newRole);
        user.Role = newRole;
        await _userManager.UpdateAsync(user);

        return true;
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        return new UserDto(user.Id, user.Email!, user.FullName, user.Role, user.IsActive, user.CreatedAt);
    }
}
