using MediatR;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Application.Features.Jobs.Queries;

public record GetJobsQuery(
    string? Search,
    string? Department,
    JobStatus? Status,
    string? Location,
    int PageNumber = 1,
    int PageSize = 20
) : IRequest<PagedList<JobDto>>;

public class GetJobsQueryHandler : IRequestHandler<GetJobsQuery, PagedList<JobDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetJobsQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PagedList<JobDto>> Handle(GetJobsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Jobs
            .AsNoTracking()
            .Where(j => !j.IsDeleted);

        // Resource-based authorization: Hiring managers can only see assigned jobs
        if (_currentUser.Role == Roles.HiringManager)
        {
            query = query.Where(j => j.HiringManagerId == _currentUser.UserId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(j => j.Title.ToLower().Contains(search) || j.Description.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.Department))
        {
            query = query.Where(j => j.Department == request.Department.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.Location))
        {
            query = query.Where(j => j.Location == request.Location.Trim());
        }

        if (request.Status.HasValue)
        {
            query = query.Where(j => j.Status == request.Status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var pageNumber = Math.Max(1, request.PageNumber);

        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new JobDto(
                j.Id,
                j.Title,
                j.Description,
                j.Department,
                j.Location,
                j.EmploymentType,
                j.SalaryMin,
                j.SalaryMax,
                j.IsSalaryDisclosed,
                j.Openings,
                j.Status,
                j.HiringManagerId,
                null,
                j.CreatedById,
                j.CreatedAt,
                j.PublishedAt,
                j.ClosedAt,
                j.Applications.Count
            ))
            .ToListAsync(cancellationToken);

        return new PagedList<JobDto>(items, totalCount, pageNumber, pageSize);
    }
}

public record GetJobByIdQuery(Guid Id) : IRequest<JobDto>;

public class GetJobByIdQueryHandler : IRequestHandler<GetJobByIdQuery, JobDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public GetJobByIdQueryHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<JobDto> Handle(GetJobByIdQuery request, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs
            .AsNoTracking()
            .Include(j => j.Applications)
            .FirstOrDefaultAsync(j => j.Id == request.Id && !j.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(JobDto), request.Id);

        if (_currentUser.Role == Roles.HiringManager && job.HiringManagerId != _currentUser.UserId)
        {
            throw new ForbiddenException("Hiring managers can only access jobs assigned to them.");
        }

        return new JobDto(
            job.Id,
            job.Title,
            job.Description,
            job.Department,
            job.Location,
            job.EmploymentType,
            job.SalaryMin,
            job.SalaryMax,
            job.IsSalaryDisclosed,
            job.Openings,
            job.Status,
            job.HiringManagerId,
            null,
            job.CreatedById,
            job.CreatedAt,
            job.PublishedAt,
            job.ClosedAt,
            job.Applications.Count
        );
    }
}
