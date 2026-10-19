using MediatR;
using Microsoft.Extensions.Logging;
using TalentFlow.Application.Common.Interfaces;

namespace TalentFlow.Application.Common.Behaviors;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(IApplicationDbContext context, ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        // Only wrap commands in transactions; queries do not require write transactions
        if (!requestName.EndsWith("Command") || _context.Database.CurrentTransaction != null)
        {
            return await next();
        }

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            _logger.LogDebug("Beginning ambient transaction for {RequestName}", requestName);

            var response = await next();

            await transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Committed ambient transaction for {RequestName}", requestName);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction failed for {RequestName}. Rolling back.", requestName);
            throw;
        }
    }
}
