using MediatR;
using Microsoft.Extensions.Logging;
using TalentFlow.Application.Common.Interfaces;

namespace TalentFlow.Application.Common.Behaviors;

public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AuditBehavior<TRequest, TResponse>> _logger;

    public AuditBehavior(ICurrentUserService currentUserService, ILogger<AuditBehavior<TRequest, TResponse>> logger)
    {
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var userId = _currentUserService.UserId ?? "Anonymous";
        var userEmail = _currentUserService.Email ?? "Unknown";

        if (requestName.EndsWith("Command"))
        {
            _logger.LogInformation("Audit: Executing command {RequestName} by User {UserId} ({UserEmail})",
                requestName, userId, userEmail);
        }

        var response = await next();

        if (requestName.EndsWith("Command"))
        {
            _logger.LogInformation("Audit: Executed command {RequestName} successfully by User {UserId} ({UserEmail})",
                requestName, userId, userEmail);
        }

        return response;
    }
}
