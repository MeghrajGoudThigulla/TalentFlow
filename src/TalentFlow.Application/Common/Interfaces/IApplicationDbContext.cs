using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TalentFlow.Domain.Entities;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Job> Jobs { get; }
    DbSet<PipelineStage> PipelineStages { get; }
    DbSet<Candidate> Candidates { get; }
    DbSet<DomainApplication> Applications { get; }
    DbSet<StageTransition> StageTransitions { get; }
    DbSet<Note> Notes { get; }
    DbSet<Interview> Interviews { get; }
    DbSet<InterviewParticipant> InterviewParticipants { get; }
    DbSet<InterviewFeedback> InterviewFeedbacks { get; }
    DbSet<OfferApproval> OfferApprovals { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
