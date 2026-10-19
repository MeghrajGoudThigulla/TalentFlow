using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TalentFlow.Domain.Entities;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.Identity;
using DomainApplication = TalentFlow.Domain.Entities.Application;

namespace TalentFlow.Infrastructure.Persistence;

public class ApplicationDbContextInitialiser
{
    private readonly ILogger<ApplicationDbContextInitialiser> _logger;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public ApplicationDbContextInitialiser(
        ILogger<ApplicationDbContextInitialiser> logger,
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _logger = logger;
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            await _context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    private async Task TrySeedAsync()
    {
        // 1. Seed Roles
        var roles = new[] { Roles.Admin, Roles.Recruiter, Roles.HiringManager };
        foreach (var role in roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                await _roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // 2. Seed Admin User
        var adminEmail = "admin@talentflow.local";
        var adminUser = await _userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "TalentFlow Administrator",
                Role = Roles.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await _userManager.CreateAsync(adminUser, "Admin@123456");
            await _userManager.AddToRoleAsync(adminUser, Roles.Admin);
        }

        // 3. Seed Recruiter User
        var recruiterEmail = "recruiter@talentflow.local";
        var recruiterUser = await _userManager.FindByEmailAsync(recruiterEmail);
        if (recruiterUser == null)
        {
            recruiterUser = new ApplicationUser
            {
                UserName = recruiterEmail,
                Email = recruiterEmail,
                FullName = "Sarah Jenkins",
                Role = Roles.Recruiter,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await _userManager.CreateAsync(recruiterUser, "Recruiter@123456");
            await _userManager.AddToRoleAsync(recruiterUser, Roles.Recruiter);
        }

        // 4. Seed Hiring Manager User
        var hmEmail = "hiringmanager@talentflow.local";
        var hmUser = await _userManager.FindByEmailAsync(hmEmail);
        if (hmUser == null)
        {
            hmUser = new ApplicationUser
            {
                UserName = hmEmail,
                Email = hmEmail,
                FullName = "Alex Chen",
                Role = Roles.HiringManager,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await _userManager.CreateAsync(hmUser, "Manager@123456");
            await _userManager.AddToRoleAsync(hmUser, Roles.HiringManager);
        }

        // 5. Seed Pipeline Stages
        if (!await _context.PipelineStages.AnyAsync())
        {
            var stages = new List<PipelineStage>
            {
                PipelineStage.Create("Applied", 1, StageKind.Normal, isTerminal: false),
                PipelineStage.Create("Screening", 2, StageKind.Normal, isTerminal: false),
                PipelineStage.Create("Interview", 3, StageKind.Normal, isTerminal: false),
                PipelineStage.Create("Offer", 4, StageKind.Normal, isTerminal: false),
                PipelineStage.Create("Hired", 5, StageKind.Hired, isTerminal: true),
                PipelineStage.Create("Rejected", 6, StageKind.Rejected, isTerminal: false)
            };

            _context.PipelineStages.AddRange(stages);
            await _context.SaveChangesAsync();
        }

        // 6. Seed Sample Jobs & Applications
        if (!await _context.Jobs.AnyAsync())
        {
            var job1 = new Job
            {
                Id = Guid.NewGuid(),
                Title = "Senior .NET Backend Architect",
                Description = "Seeking an experienced .NET engineer proficient in ASP.NET Core 8, Clean Architecture, CQRS with MediatR, EF Core, and high-performance Web APIs.",
                Department = "Engineering",
                Location = "Hyderabad, India (Hybrid)",
                EmploymentType = "Full-time",
                SalaryMin = 2200000,
                SalaryMax = 3200000,
                IsSalaryDisclosed = true,
                Openings = 2,
                Status = JobStatus.Open,
                HiringManagerId = hmUser.Id,
                CreatedById = recruiterUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-14),
                PublishedAt = DateTime.UtcNow.AddDays(-13)
            };

            var job2 = new Job
            {
                Id = Guid.NewGuid(),
                Title = "Full Stack Engineer (C# / Web)",
                Description = "Join our product team building scalable enterprise ATS solutions with ASP.NET Core, SignalR real-time streaming, and modern frontends.",
                Department = "Engineering",
                Location = "Remote, India",
                EmploymentType = "Full-time",
                SalaryMin = 1500000,
                SalaryMax = 2400000,
                IsSalaryDisclosed = true,
                Openings = 3,
                Status = JobStatus.Open,
                HiringManagerId = hmUser.Id,
                CreatedById = recruiterUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                PublishedAt = DateTime.UtcNow.AddDays(-9)
            };

            var job3 = new Job
            {
                Id = Guid.NewGuid(),
                Title = "Product Manager - HR Tech",
                Description = "Lead roadmaps for next-generation talent acquisition and workflow automation platforms.",
                Department = "Product",
                Location = "Bangalore, India",
                EmploymentType = "Full-time",
                SalaryMin = 2500000,
                SalaryMax = 3500000,
                IsSalaryDisclosed = false,
                Openings = 1,
                Status = JobStatus.Draft,
                HiringManagerId = hmUser.Id,
                CreatedById = recruiterUser.Id,
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            };

            _context.Jobs.AddRange(job1, job2, job3);
            await _context.SaveChangesAsync();

            // Seed candidates & applications for job1
            var stages = await _context.PipelineStages.OrderBy(s => s.Order).ToListAsync();
            var appliedStage = stages.First(s => s.Name == "Applied");
            var screeningStage = stages.First(s => s.Name == "Screening");
            var interviewStage = stages.First(s => s.Name == "Interview");

            var c1 = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = "Meghraj Thigulla",
                Email = "meghraj.sample@talentflow.local",
                Phone = "+91 79972 21772",
                Source = "Direct Application",
                CreatedAt = DateTime.UtcNow.AddDays(-5)
            };

            var c2 = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = "Priya Sharma",
                Email = "priya.sharma@talentflow.local",
                Phone = "+91 98765 43210",
                Source = "LinkedIn Referral",
                CreatedAt = DateTime.UtcNow.AddDays(-4)
            };

            var c3 = new Candidate
            {
                Id = Guid.NewGuid(),
                FullName = "David Miller",
                Email = "david.miller@talentflow.local",
                Phone = "+1 415 555 2671",
                Source = "Career Page",
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            };

            _context.Candidates.AddRange(c1, c2, c3);
            await _context.SaveChangesAsync();

            var app1 = new DomainApplication
            {
                Id = Guid.NewGuid(),
                JobId = job1.Id,
                CandidateId = c1.Id,
                CurrentStageId = interviewStage.Id,
                CoverNote = "Passionate full-stack & backend developer with extensive experience in enterprise systems and real-time architectures.",
                MatchScore = 95,
                MatchedSkills = "C#, .NET, ASP.NET Core, Clean Architecture, CQRS, SignalR, SQL Server",
                AppliedAt = DateTime.UtcNow.AddDays(-5),
                RowVersion = Guid.NewGuid().ToByteArray()
            };

            var app2 = new DomainApplication
            {
                Id = Guid.NewGuid(),
                JobId = job1.Id,
                CandidateId = c2.Id,
                CurrentStageId = screeningStage.Id,
                CoverNote = "Strong hands-on background in relational databases, ORMs, and distributed services.",
                MatchScore = 86,
                MatchedSkills = "C#, .NET, SQL Server, Entity Framework, REST, API",
                AppliedAt = DateTime.UtcNow.AddDays(-4),
                RowVersion = Guid.NewGuid().ToByteArray()
            };

            var app3 = new DomainApplication
            {
                Id = Guid.NewGuid(),
                JobId = job1.Id,
                CandidateId = c3.Id,
                CurrentStageId = appliedStage.Id,
                CoverNote = "Excited about modern .NET 8 architecture and high-scale ATS systems.",
                MatchScore = 78,
                MatchedSkills = "C#, .NET, Docker, Testing",
                AppliedAt = DateTime.UtcNow.AddDays(-2),
                RowVersion = Guid.NewGuid().ToByteArray()
            };

            _context.Applications.AddRange(app1, app2, app3);
            await _context.SaveChangesAsync();

            // Notes and interview for app1
            _context.Notes.Add(new Note
            {
                Id = Guid.NewGuid(),
                ApplicationId = app1.Id,
                AuthorId = recruiterUser.Id,
                AuthorName = recruiterUser.FullName,
                Body = "Candidate had excellent answers during initial screening. Recommending for technical interview round.",
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            });

            var interview1 = new Interview
            {
                Id = Guid.NewGuid(),
                ApplicationId = app1.Id,
                StartsAt = DateTime.UtcNow.AddDays(1).Date.AddHours(14), // Tomorrow at 2 PM
                DurationMinutes = 60,
                Type = InterviewType.Video,
                LocationOrLink = "https://meet.talentflow.local/tech-interview-01",
                Status = InterviewStatus.Scheduled,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            };

            interview1.Participants.Add(new InterviewParticipant
            {
                InterviewId = interview1.Id,
                InterviewerId = hmUser.Id,
                InterviewerName = hmUser.FullName
            });

            _context.Interviews.Add(interview1);
            await _context.SaveChangesAsync();
        }
    }
}
