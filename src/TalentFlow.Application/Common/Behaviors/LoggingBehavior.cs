using MediatR;
using Microsoft.Extensions.Logging;
using TalentFlow.Application.Common.Interfaces;

namespace TalentFlow.Application.Common.Behaviors;

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<TRequest> _logger;
    private readonly ICurrentUserService _currentUserService;

    public LoggingBehavior(ILogger<TRequest> logger, ICurrentUserService currentUserService)
    {
        _logger = logger;
        _currentUserService = currentUserService;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var userId = _currentUserService.UserId ?? "Anonymous";

        _logger.LogInformation("Handling {RequestName} by User {UserId}", requestName, userId);

        var response = await next();

        _logger.LogInformation("Handled {RequestName} successfully", requestName);

        return response;
    }
}
