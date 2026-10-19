using MediatR;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Application.Features.Reports;

public record GetPipelineReportQuery(Guid JobId) : IRequest<PipelineReportDto>;

public class GetPipelineReportQueryHandler : IRequestHandler<GetPipelineReportQuery, PipelineReportDto>
{
    private readonly IApplicationDbContext _context;

    public GetPipelineReportQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PipelineReportDto> Handle(GetPipelineReportQuery request, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId && !j.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), request.JobId);

        var stages = await _context.PipelineStages
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.Order)
            .ToListAsync(cancellationToken);

        var counts = await _context.Applications
            .AsNoTracking()
            .Where(a => a.JobId == request.JobId)
            .GroupBy(a => a.CurrentStageId)
            .Select(g => new { StageId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var stageCountsDict = new Dictionary<string, int>();
        int totalApps = 0;

        foreach (var stage in stages)
        {
            var c = counts.FirstOrDefault(x => x.StageId == stage.Id)?.Count ?? 0;
            stageCountsDict[stage.Name] = c;
            totalApps += c;
        }

        return new PipelineReportDto(job.Id, job.Title, stageCountsDict, totalApps);
    }
}

public record GetTimeToHireReportQuery(Guid? JobId) : IRequest<List<TimeToHireReportDto>>;

public class GetTimeToHireReportQueryHandler : IRequestHandler<GetTimeToHireReportQuery, List<TimeToHireReportDto>>
{
    private readonly IApplicationDbContext _context;

    public GetTimeToHireReportQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<TimeToHireReportDto>> Handle(GetTimeToHireReportQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Jobs
            .AsNoTracking()
            .Where(j => !j.IsDeleted);

        if (request.JobId.HasValue)
        {
            query = query.Where(j => j.Id == request.JobId.Value);
        }

        var jobs = await query.ToListAsync(cancellationToken);
        var result = new List<TimeToHireReportDto>();

        foreach (var job in jobs)
        {
            var hiredApps = await _context.Applications
                .AsNoTracking()
                .Include(a => a.CurrentStage)
                .Include(a => a.StageTransitions)
                .Where(a => a.JobId == job.Id && a.CurrentStage.Kind == StageKind.Hired)
                .ToListAsync(cancellationToken);

            double totalDays = 0;
            int count = hiredApps.Count;

            foreach (var app in hiredApps)
            {
                var hireTransition = app.StageTransitions
                    .OrderByDescending(t => t.OccurredAt)
                    .FirstOrDefault();

                var hireDate = hireTransition?.OccurredAt ?? DateTime.UtcNow;
                totalDays += (hireDate - app.AppliedAt).TotalDays;
            }

            double avg = count > 0 ? Math.Round(totalDays / count, 1) : 0.0;
            result.Add(new TimeToHireReportDto(job.Id, job.Title, avg, count));
        }

        return result;
    }
}
