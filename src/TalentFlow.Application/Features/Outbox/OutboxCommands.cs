using MediatR;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Application.Features.Outbox;

public record GetFailedOutboxQuery : IRequest<List<OutboxMessageDto>>;

public class GetFailedOutboxQueryHandler : IRequestHandler<GetFailedOutboxQuery, List<OutboxMessageDto>>
{
    private readonly IApplicationDbContext _context;

    public GetFailedOutboxQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<OutboxMessageDto>> Handle(GetFailedOutboxQuery request, CancellationToken cancellationToken)
    {
        return await _context.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Status == OutboxMessageStatus.Failed)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new OutboxMessageDto(
                m.Id, m.Type, m.ToAddress, m.Subject, m.DedupeKey,
                m.Status, m.Attempts, m.NextAttemptAt, m.LastError,
                m.CreatedAt, m.SentAt
            ))
            .ToListAsync(cancellationToken);
    }
}

public record RetryOutboxMessageCommand(Guid Id) : IRequest<Unit>;

public class RetryOutboxMessageCommandHandler : IRequestHandler<RetryOutboxMessageCommand, Unit>
{
    private readonly IApplicationDbContext _context;

    public RetryOutboxMessageCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Unit> Handle(RetryOutboxMessageCommand request, CancellationToken cancellationToken)
    {
        var msg = await _context.OutboxMessages.FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(OutboxMessage), request.Id);

        msg.Status = OutboxMessageStatus.Pending;
        msg.NextAttemptAt = DateTime.UtcNow;
        msg.Attempts = 0;
        msg.LastError = null;
        msg.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
