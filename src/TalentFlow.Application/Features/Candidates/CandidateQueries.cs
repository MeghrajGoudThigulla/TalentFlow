using MediatR;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Entities;

namespace TalentFlow.Application.Features.Candidates;

public record SearchCandidatesQuery(
    string? Search,
    Guid? StageId,
    Guid? JobId,
    int PageNumber = 1,
    int PageSize = 20
) : IRequest<PagedList<CandidateDto>>;

public class SearchCandidatesQueryHandler : IRequestHandler<SearchCandidatesQuery, PagedList<CandidateDto>>
{
    private readonly IApplicationDbContext _context;

    public SearchCandidatesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedList<CandidateDto>> Handle(SearchCandidatesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Candidates
            .AsNoTracking()
            .Include(c => c.Applications)
                .ThenInclude(a => a.CurrentStage)
            .Where(c => !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(c => c.FullName.ToLower().Contains(search) || c.Email.ToLower().Contains(search));
        }

        if (request.JobId.HasValue)
        {
            query = query.Where(c => c.Applications.Any(a => a.JobId == request.JobId.Value));
        }

        if (request.StageId.HasValue)
        {
            query = query.Where(c => c.Applications.Any(a => a.CurrentStageId == request.StageId.Value));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(c => new CandidateDto(
                c.Id,
                c.FullName,
                c.Email,
                c.Phone,
                c.Source,
                c.CreatedAt,
                c.Applications.Select(a => new ApplicationCardDto(
                    a.Id,
                    a.JobId,
                    a.CandidateId,
                    c.FullName,
                    c.Email,
                    c.Phone,
                    a.CurrentStageId,
                    a.CurrentStage.Name,
                    a.AppliedAt,
                    a.MatchScore,
                    a.MatchedSkills,
                    a.Notes.Count,
                    a.Interviews.Count,
                    a.OfferApproval != null,
                    Convert.ToBase64String(a.RowVersion)
                )).ToList()
            ))
            .ToListAsync(cancellationToken);

        return new PagedList<CandidateDto>(items, total, request.PageNumber, request.PageSize);
    }
}

public record GetCandidateByIdQuery(Guid Id) : IRequest<CandidateDto>;

public class GetCandidateByIdQueryHandler : IRequestHandler<GetCandidateByIdQuery, CandidateDto>
{
    private readonly IApplicationDbContext _context;

    public GetCandidateByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CandidateDto> Handle(GetCandidateByIdQuery request, CancellationToken cancellationToken)
    {
        var c = await _context.Candidates
            .AsNoTracking()
            .Include(c => c.Applications)
                .ThenInclude(a => a.CurrentStage)
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Candidate), request.Id);

        return new CandidateDto(
            c.Id,
            c.FullName,
            c.Email,
            c.Phone,
            c.Source,
            c.CreatedAt,
            c.Applications.Select(a => new ApplicationCardDto(
                a.Id,
                a.JobId,
                a.CandidateId,
                c.FullName,
                c.Email,
                c.Phone,
                a.CurrentStageId,
                a.CurrentStage.Name,
                a.AppliedAt,
                a.MatchScore,
                a.MatchedSkills,
                a.Notes.Count,
                a.Interviews.Count,
                a.OfferApproval != null,
                Convert.ToBase64String(a.RowVersion)
            )).ToList()
        );
    }
}
