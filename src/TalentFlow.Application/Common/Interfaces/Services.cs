namespace TalentFlow.Application.Common.Interfaces;

public interface ICurrentUserService
{
    string? UserId { get; }
    string? Email { get; }
    string? FullName { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
}

public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}

public interface IFileStorageService
{
    Task<(string StoredPath, string OriginalFileName)> SaveResumeAsync(Stream fileStream, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<(Stream ContentStream, string ContentType, string FileName)?> GetResumeAsync(string storedPath, CancellationToken cancellationToken = default);
}

public interface IPipelineNotifier
{
    Task NotifyApplicationCreated(Guid jobId, object applicationSummary);
    Task NotifyApplicationMoved(Guid jobId, Guid applicationId, Guid fromStageId, Guid toStageId, string candidateName, string toStageName);
    Task NotifyNoteAdded(Guid jobId, Guid applicationId, object noteSummary);
    Task NotifyInterviewScheduled(Guid jobId, Guid applicationId, object interviewSummary);
}

public interface IDateTime
{
    DateTime UtcNow { get; }
}

public interface IIdentityService
{
    Task<(bool Succeeded, string UserId, string? Error)> CreateUserAsync(string email, string password, string fullName, string role);
    Task<(bool Succeeded, string UserId, string FullName, string Role, string? Error)> ValidateUserCredentialsAsync(string email, string password);
    Task<List<UserDto>> GetUsersAsync();
    Task<bool> UpdateUserRoleAsync(string userId, string newRole);
    Task<UserDto?> GetUserByIdAsync(string userId);
}

public record UserDto(string Id, string Email, string FullName, string Role, bool IsActive, DateTime CreatedAt);
