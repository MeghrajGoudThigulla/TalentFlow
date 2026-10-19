using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Exceptions;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Common.Models;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;

namespace TalentFlow.Application.Features.Jobs.Commands;

public record CreateJobCommand(
    string Title,
    string Description,
    string Department,
    string Location,
    string EmploymentType,
    decimal? SalaryMin,
    decimal? SalaryMax,
    bool IsSalaryDisclosed,
    int Openings,
    string? HiringManagerId
) : IRequest<JobDto>;

public class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty();
        RuleFor(x => x.Department).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Location).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Openings).GreaterThan(0);
        RuleFor(x => x.SalaryMax)
            .GreaterThanOrEqualTo(x => x.SalaryMin)
            .When(x => x.SalaryMin.HasValue && x.SalaryMax.HasValue)
            .WithMessage("SalaryMax must be greater than or equal to SalaryMin.");
    }
}

public class CreateJobCommandHandler : IRequestHandler<CreateJobCommand, JobDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public CreateJobCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<JobDto> Handle(CreateJobCommand request, CancellationToken cancellationToken)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Description = request.Description,
            Department = request.Department.Trim(),
            Location = request.Location.Trim(),
            EmploymentType = request.EmploymentType,
            SalaryMin = request.SalaryMin,
            SalaryMax = request.SalaryMax,
            IsSalaryDisclosed = request.IsSalaryDisclosed,
            Openings = request.Openings,
            Status = JobStatus.Draft,
            HiringManagerId = request.HiringManagerId,
            CreatedById = _currentUser.UserId ?? "System",
            CreatedAt = DateTime.UtcNow
        };

        _context.Jobs.Add(job);

        _context.AuditLogs.Add(new AuditLog
        {
            ActorId = _currentUser.UserId,
            ActorEmail = _currentUser.Email,
            EntityType = nameof(Job),
            EntityId = job.Id.ToString(),
            Action = "CreatedJob",
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { job.Title, job.Department, job.Status }),
            OccurredAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new JobDto(
            job.Id, job.Title, job.Description, job.Department, job.Location,
            job.EmploymentType, job.SalaryMin, job.SalaryMax, job.IsSalaryDisclosed,
            job.Openings, job.Status, job.HiringManagerId, null, job.CreatedById,
            job.CreatedAt, job.PublishedAt, job.ClosedAt, 0);
    }
}

public record UpdateJobCommand(
    Guid Id,
    string Title,
    string Description,
    string Department,
    string Location,
    string EmploymentType,
    decimal? SalaryMin,
    decimal? SalaryMax,
    bool IsSalaryDisclosed,
    int Openings,
    string? HiringManagerId
) : IRequest<JobDto>;

public class UpdateJobCommandHandler : IRequestHandler<UpdateJobCommand, JobDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public UpdateJobCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<JobDto> Handle(UpdateJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs
            .Include(j => j.Applications)
            .FirstOrDefaultAsync(j => j.Id == request.Id && !j.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), request.Id);

        // IDOR protection: HiringManager can only edit own jobs
        if (_currentUser.Role == Roles.HiringManager && job.HiringManagerId != _currentUser.UserId)
        {
            throw new ForbiddenException("Hiring managers can only edit jobs assigned to them.");
        }

        job.Title = request.Title.Trim();
        job.Description = request.Description;
        job.Department = request.Department.Trim();
        job.Location = request.Location.Trim();
        job.EmploymentType = request.EmploymentType;
        job.SalaryMin = request.SalaryMin;
        job.SalaryMax = request.SalaryMax;
        job.IsSalaryDisclosed = request.IsSalaryDisclosed;
        job.Openings = request.Openings;
        job.HiringManagerId = request.HiringManagerId;
        job.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new JobDto(
            job.Id, job.Title, job.Description, job.Department, job.Location,
            job.EmploymentType, job.SalaryMin, job.SalaryMax, job.IsSalaryDisclosed,
            job.Openings, job.Status, job.HiringManagerId, null, job.CreatedById,
            job.CreatedAt, job.PublishedAt, job.ClosedAt, job.Applications.Count);
    }
}

public record PublishJobCommand(Guid Id) : IRequest<JobDto>;

public class PublishJobCommandHandler : IRequestHandler<PublishJobCommand, JobDto>
{
    private readonly IApplicationDbContext _context;

    public PublishJobCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<JobDto> Handle(PublishJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs
            .Include(j => j.Applications)
            .FirstOrDefaultAsync(j => j.Id == request.Id && !j.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), request.Id);

        job.Publish();
        await _context.SaveChangesAsync(cancellationToken);

        return new JobDto(
            job.Id, job.Title, job.Description, job.Department, job.Location,
            job.EmploymentType, job.SalaryMin, job.SalaryMax, job.IsSalaryDisclosed,
            job.Openings, job.Status, job.HiringManagerId, null, job.CreatedById,
            job.CreatedAt, job.PublishedAt, job.ClosedAt, job.Applications.Count);
    }
}

public record CloseJobCommand(Guid Id) : IRequest<JobDto>;

public class CloseJobCommandHandler : IRequestHandler<CloseJobCommand, JobDto>
{
    private readonly IApplicationDbContext _context;

    public CloseJobCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<JobDto> Handle(CloseJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs
            .Include(j => j.Applications)
            .FirstOrDefaultAsync(j => j.Id == request.Id && !j.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), request.Id);

        job.Close();
        await _context.SaveChangesAsync(cancellationToken);

        return new JobDto(
            job.Id, job.Title, job.Description, job.Department, job.Location,
            job.EmploymentType, job.SalaryMin, job.SalaryMax, job.IsSalaryDisclosed,
            job.Openings, job.Status, job.HiringManagerId, null, job.CreatedById,
            job.CreatedAt, job.PublishedAt, job.ClosedAt, job.Applications.Count);
    }
}

public record DeleteJobCommand(Guid Id) : IRequest<Unit>;

public class DeleteJobCommandHandler : IRequestHandler<DeleteJobCommand, Unit>
{
    private readonly IApplicationDbContext _context;

    public DeleteJobCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Unit> Handle(DeleteJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _context.Jobs.FirstOrDefaultAsync(j => j.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), request.Id);

        job.IsDeleted = true;
        job.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
