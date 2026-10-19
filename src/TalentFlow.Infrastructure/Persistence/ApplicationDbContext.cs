using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Domain.Entities;
using TalentFlow.Infrastructure.Identity;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<DomainApplication> Applications => Set<DomainApplication>();
    public DbSet<StageTransition> StageTransitions => Set<StageTransition>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<InterviewParticipant> InterviewParticipants => Set<InterviewParticipant>();
    public DbSet<InterviewFeedback> InterviewFeedbacks => Set<InterviewFeedback>();
    public DbSet<OfferApproval> OfferApprovals => Set<OfferApproval>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Global query filters for soft deletion
        builder.Entity<Job>().HasQueryFilter(j => !j.IsDeleted);
        builder.Entity<Candidate>().HasQueryFilter(c => !c.IsDeleted);
        builder.Entity<PipelineStage>().HasQueryFilter(s => !s.IsDeleted);

        // Job indexes & configuration
        builder.Entity<Job>(b =>
        {
            b.HasKey(j => j.Id);
            b.Property(j => j.Title).IsRequired().HasMaxLength(200);
            b.Property(j => j.Department).IsRequired().HasMaxLength(100);
            b.Property(j => j.Location).IsRequired().HasMaxLength(100);
            b.Property(j => j.SalaryMin).HasColumnType("decimal(18,2)");
            b.Property(j => j.SalaryMax).HasColumnType("decimal(18,2)");
            b.HasIndex(j => new { j.Status, j.PublishedAt });
        });

        // PipelineStage configuration
        builder.Entity<PipelineStage>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Name).IsRequired().HasMaxLength(100);
            b.HasIndex(s => s.Order);
        });

        // Candidate configuration
        builder.Entity<Candidate>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.FullName).IsRequired().HasMaxLength(150);
            b.Property(c => c.Email).IsRequired().HasMaxLength(200);
            b.HasIndex(c => c.Email).IsUnique();
        });

        // Application configuration with ConcurrencyToken / RowVersion
        builder.Entity<DomainApplication>(b =>
        {
            b.HasKey(a => a.Id);
            b.HasIndex(a => new { a.JobId, a.CandidateId }).IsUnique();
            b.HasIndex(a => new { a.JobId, a.CurrentStageId });

            b.Property(a => a.RowVersion)
                .IsConcurrencyToken();

            b.HasOne(a => a.Job)
                .WithMany(j => j.Applications)
                .HasForeignKey(a => a.JobId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(a => a.Candidate)
                .WithMany(c => c.Applications)
                .HasForeignKey(a => a.CandidateId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(a => a.CurrentStage)
                .WithMany()
                .HasForeignKey(a => a.CurrentStageId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(a => a.OfferApproval)
                .WithOne(o => o.Application)
                .HasForeignKey<OfferApproval>(o => o.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // StageTransition configuration
        builder.Entity<StageTransition>(b =>
        {
            b.HasKey(t => t.Id);
            b.HasOne(t => t.Application)
                .WithMany(a => a.StageTransitions)
                .HasForeignKey(t => t.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(t => t.FromStage)
                .WithMany()
                .HasForeignKey(t => t.FromStageId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(t => t.ToStage)
                .WithMany()
                .HasForeignKey(t => t.ToStageId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Note configuration
        builder.Entity<Note>(b =>
        {
            b.HasKey(n => n.Id);
            b.HasOne(n => n.Application)
                .WithMany(a => a.Notes)
                .HasForeignKey(n => n.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Interview configuration
        builder.Entity<Interview>(b =>
        {
            b.HasKey(i => i.Id);
            b.HasOne(i => i.Application)
                .WithMany(a => a.Interviews)
                .HasForeignKey(i => i.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // InterviewParticipant composite key
        builder.Entity<InterviewParticipant>(b =>
        {
            b.HasKey(ip => new { ip.InterviewId, ip.InterviewerId });
            b.HasOne(ip => ip.Interview)
                .WithMany(i => i.Participants)
                .HasForeignKey(ip => ip.InterviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // InterviewFeedback configuration
        builder.Entity<InterviewFeedback>(b =>
        {
            b.HasKey(f => f.Id);
            b.HasIndex(f => new { f.InterviewId, f.InterviewerId }).IsUnique();
            b.HasOne(f => f.Interview)
                .WithMany(i => i.Feedbacks)
                .HasForeignKey(f => f.InterviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // OutboxMessage configuration
        builder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(o => o.Id);
            b.HasIndex(o => new { o.Status, o.NextAttemptAt });
            b.Property(o => o.DedupeKey).HasMaxLength(200);
        });

        // AuditLog configuration
        builder.Entity<AuditLog>(b =>
        {
            b.HasKey(a => a.Id);
            b.HasIndex(a => new { a.EntityType, a.EntityId });
            b.HasIndex(a => a.OccurredAt);
        });

        // RefreshToken configuration
        builder.Entity<RefreshToken>(b =>
        {
            b.HasKey(r => r.Id);
            b.HasIndex(r => r.TokenHash).IsUnique();
            b.HasIndex(r => r.UserId);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<DomainApplication>())
        {
            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                if (entry.Entity.RowVersion == null || entry.Entity.RowVersion.Length == 0)
                {
                    entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
                }
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
