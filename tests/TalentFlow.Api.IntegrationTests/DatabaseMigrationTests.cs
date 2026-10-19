using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TalentFlow.Infrastructure.Persistence;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class DatabaseMigrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DatabaseMigrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Database_ShouldHaveAppliedMigrations_WithNoPendingMigrations()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        appliedMigrations.Should().Contain(m => m.Contains("InitialCreate"), "InitialCreate migration must be applied per ARCH-02");

        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
        pendingMigrations.Should().BeEmpty("there should be no pending migrations after InitialiseAsync");
    }
}
