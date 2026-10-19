using MediatR;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;

namespace TalentFlow.Application.Features.Audit;

public record GetAuditLogsQuery(
    string? EntityType,
    string? EntityId,
    int PageNumber = 1,
    int PageSize = 30
) : IRequest<PagedList<AuditLogDto>>;

public class GetAuditLogsQueryHandler : IRequestHandler<GetAuditLogsQuery, PagedList<AuditLogDto>>
{
    private readonly IApplicationDbContext _context;

    public GetAuditLogsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedList<AuditLogDto>> Handle(GetAuditLogsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            query = query.Where(a => a.EntityType == request.EntityType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.EntityId))
        {
            query = query.Where(a => a.EntityId == request.EntityId.Trim());
        }

        var total = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var pageNumber = Math.Max(1, request.PageNumber);

        var items = await query
            .OrderByDescending(a => a.OccurredAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(
                a.Id, a.ActorId, a.ActorEmail, a.EntityType, a.EntityId,
                a.Action, a.BeforeJson, a.AfterJson, a.CorrelationId, a.OccurredAt
            ))
            .ToListAsync(cancellationToken);

        return new PagedList<AuditLogDto>(items, total, pageNumber, pageSize);
    }
}
