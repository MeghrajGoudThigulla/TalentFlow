using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Application.Features.Auth;
using TalentFlow.Domain.Enums;
using TalentFlow.Infrastructure.BackgroundWorkers;
using TalentFlow.Infrastructure.Identity;
using TalentFlow.Infrastructure.Persistence;
using TalentFlow.Infrastructure.RealTime;
using TalentFlow.Infrastructure.Services;

namespace TalentFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Database Context - default to SQLite for zero-setup local dev/test or SQL Server if connection string is configured
        var useSqlServer = configuration.GetValue<bool>("UseSqlServer");
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=talentflow.db";

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (useSqlServer)
            {
                options.UseSqlServer(connectionString, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
            }
            else
            {
                options.UseSqlite(connectionString, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
            }
        });

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ApplicationDbContextInitialiser>();

        // 2. Identity Configuration
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // 3. JWT Authentication
        var secret = configuration["JwtSettings:Secret"] ?? "TalentFlowDefaultSuperSecretKey123456789!#%&";
        var issuer = configuration["JwtSettings:Issuer"] ?? "TalentFlowApi";
        var audience = configuration["JwtSettings:Audience"] ?? "TalentFlowClient";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            // SignalR token authentication via query string
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/pipeline"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        // 4. Role Authorization Policies
        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAdmin", policy => policy.RequireRole(Roles.Admin));
            options.AddPolicy("RequireRecruiterOrAdmin", policy => policy.RequireRole(Roles.Admin, Roles.Recruiter));
            options.AddPolicy("RequireHiringManagerOrAbove", policy => policy.RequireRole(Roles.Admin, Roles.Recruiter, Roles.HiringManager));
        });

        // 5. Infrastructure Services
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
        services.AddScoped<IEmailSender, EmailSender>();
        services.AddSingleton<IDateTime, DateTimeService>();

        // 6. Real-Time SignalR
        services.AddSignalR();
        services.AddScoped<IPipelineNotifier, PipelineNotifier>();

        // 7. Hosted Outbox Background Processor
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
