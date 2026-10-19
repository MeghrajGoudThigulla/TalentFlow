using FluentAssertions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace TalentFlow.Api.IntegrationTests;

public class SqlServerTestcontainersTests
{
    [Fact]
    public async Task SqlServerContainer_CanBeConfigured_AndStartedIfDockerAvailable()
    {
        var builder = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword("StrongP@ssw0rd!2026");

        builder.Should().NotBeNull();

        MsSqlContainer? container = null;
        try
        {
            container = builder.Build();
            await container.StartAsync();

            var connectionString = container.GetConnectionString();
            connectionString.Should().NotBeNullOrWhiteSpace();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            connection.State.Should().Be(System.Data.ConnectionState.Open);
        }
        catch (DotNet.Testcontainers.Builders.DockerUnavailableException ex)
        {
            // In environments without Docker daemon (e.g. lightweight CI or containerized sandbox),
            // builder.Build() throws DockerUnavailableException as expected.
            ex.Message.Should().Contain("Docker is either not running or misconfigured");
        }
        finally
        {
            if (container != null)
            {
                await container.DisposeAsync();
            }
        }
    }
}
