using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using TalentFlow.Infrastructure.Identity;
using TalentFlow.Infrastructure.Persistence;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class EnvironmentSeedingTests
{
    [Fact]
    public async Task ProductionEnvironment_ShouldNotSeedDefaultAdminUser()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("JwtSettings:Secret", "ProductionTestSecretKeyThatIsAtLeast32BytesLong!");

                builder.ConfigureServices(services =>
                {
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

                    services.AddDbContext<ApplicationDbContext>(options =>
                    {
                        options.UseSqlite(connection);
                    });
                });
            });

        // Trigger host startup
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // In Production, admin@talentflow.local should NOT have been seeded
        var adminUser = await userManager.FindByEmailAsync("admin@talentflow.local");
        adminUser.Should().BeNull("default admin user must never be seeded in non-Development environments");

        var recruiterUser = await userManager.FindByEmailAsync("recruiter@talentflow.local");
        recruiterUser.Should().BeNull("sample users must not be seeded in non-Development environments");
    }
}
