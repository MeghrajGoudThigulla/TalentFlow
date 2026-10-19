using MediatR;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Application.Features.PipelineStages;

public record GetPipelineStagesQuery : IRequest<List<PipelineStageDto>>;

public class GetPipelineStagesQueryHandler : IRequestHandler<GetPipelineStagesQuery, List<PipelineStageDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPipelineStagesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<PipelineStageDto>> Handle(GetPipelineStagesQuery request, CancellationToken cancellationToken)
    {
        return await _context.PipelineStages
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.Order)
            .Select(s => new PipelineStageDto(s.Id, s.Name, s.Order, s.IsTerminal, s.Kind))
            .ToListAsync(cancellationToken);
    }
}

public record UpdatePipelineStagesCommand(List<PipelineStageDto> Stages) : IRequest<List<PipelineStageDto>>;

public class UpdatePipelineStagesCommandHandler : IRequestHandler<UpdatePipelineStagesCommand, List<PipelineStageDto>>
{
    private readonly IApplicationDbContext _context;

    public UpdatePipelineStagesCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<PipelineStageDto>> Handle(UpdatePipelineStagesCommand request, CancellationToken cancellationToken)
    {
        var existingStages = await _context.PipelineStages.ToListAsync(cancellationToken);

        foreach (var stageDto in request.Stages)
        {
            var match = existingStages.FirstOrDefault(s => s.Id == stageDto.Id);
            if (match != null)
            {
                match.Name = stageDto.Name.Trim();
                match.Order = stageDto.Order;
                match.IsTerminal = stageDto.IsTerminal;
                match.Kind = stageDto.Kind;
                match.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.PipelineStages.Add(new PipelineStage
                {
                    Id = stageDto.Id == Guid.Empty ? Guid.NewGuid() : stageDto.Id,
                    Name = stageDto.Name.Trim(),
                    Order = stageDto.Order,
                    IsTerminal = stageDto.IsTerminal,
                    Kind = stageDto.Kind,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return await _context.PipelineStages
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .OrderBy(s => s.Order)
            .Select(s => new PipelineStageDto(s.Id, s.Name, s.Order, s.IsTerminal, s.Kind))
            .ToListAsync(cancellationToken);
    }
}
