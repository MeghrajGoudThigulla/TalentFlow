using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Data.Common;
using System.Net.Http.Headers;
using TalentFlow.Application.Features.Auth;
using TalentFlow.Infrastructure.Identity;
using TalentFlow.Infrastructure.Persistence;

namespace TalentFlow.Api.IntegrationTests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"test_{Guid.NewGuid():N}.db";
    private SqliteConnection? _connection;

    public string TestJwtSecret { get; } = "TestIntegrationSecretKeyThatIsAtLeast32BytesLong123456789!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = TestJwtSecret,
                ["JwtSettings:Issuer"] = "TalentFlowApi",
                ["JwtSettings:Audience"] = "TalentFlowClient",
                ["JwtSettings:ExpiryMinutes"] = "15",
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                ["Cors:AllowedOrigins:1"] = "https://app.talentflow.local"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove existing ApplicationDbContext descriptor
            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            var dbConnectionDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbConnection));
            if (dbConnectionDescriptor != null)
            {
                services.Remove(dbConnectionDescriptor);
            }

            // Create in-memory SQLite connection kept open for duration of factory
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                options.UseSqlite(_connection);
            });

            services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.IssuerSigningKey =
                    new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(TestJwtSecret));
                options.TokenValidationParameters.ValidIssuer = "TalentFlowApi";
                options.TokenValidationParameters.ValidAudience = "TalentFlowClient";
            });
        });
    }

    public HttpClient CreateAuthenticatedClient(string role, string userId, string email, string fullName)
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var (token, _) = jwtGenerator.GenerateAccessToken(userId, email, fullName, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public string GenerateToken(string role, string userId, string email, string fullName)
    {
        using var scope = Services.CreateScope();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var (token, _) = jwtGenerator.GenerateAccessToken(userId, email, fullName, role);
        return token;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
        }
    }
}
