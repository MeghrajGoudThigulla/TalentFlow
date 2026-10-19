using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;
using TalentFlow.Api.Middleware;
using TalentFlow.Api.Services;
using TalentFlow.Application;
using TalentFlow.Application.Common.Interfaces;
using TalentFlow.Infrastructure;
using TalentFlow.Infrastructure.Persistence;
using TalentFlow.Infrastructure.RealTime;

var builder = WebApplication.CreateBuilder(args);

// Dynamically bind to cloud provider PORT (Render, Koyeb, Fly.io, Railway) if specified
var envPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(envPort))
{
    builder.WebHost.UseUrls($"http://+:{envPort}");
}

// Add Application and Infrastructure layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Add API-level services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger with JWT Bearer authentication
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TalentFlow ATS API",
        Version = "v1",
        Description = "Enterprise Applicant Tracking System API built with Clean Architecture, CQRS, EF Core, and SignalR."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// CORS configuration allowing frontend dashboard and SignalR
builder.Services.AddCors(options =>
{
    options.AddPolicy("TalentFlowCors", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Rate limiting (FR-4 & Non-functional)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("auth-rate-limit", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("apply-rate-limit", opt =>
    {
        opt.PermitLimit = 20;
        opt.Window = TimeSpan.FromHours(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

var app = builder.Build();

// Seed database on startup
using (var scope = app.Services.CreateScope())
{
    var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
    await initialiser.InitialiseAsync();
    await initialiser.SeedAsync();
}

// Exception handling middleware (RFC 7807 ProblemDetails)
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Enable Swagger in all environments for demonstration
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "TalentFlow ATS API v1");
    c.RoutePrefix = "swagger";
});

// Serve frontend static assets (Kanban Board UI)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("TalentFlowCors");
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// SignalR Real-Time Hub
app.MapHub<PipelineHub>("/hubs/pipeline");

// Health check probes (FR-348)
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }));
app.MapGet("/health/ready", async (ApplicationDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? Results.Ok(new { status = "Ready", database = "Connected", timestamp = DateTime.UtcNow })
        : Results.Problem("Database unavailable", statusCode: 503);
});

app.Run();

public partial class Program { }
