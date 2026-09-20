using bondstone_backend.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace bondstone_backend.Api.IntegrationTests;

public class ConnectionResilienceMigrationTests : IAsyncLifetime
{
    private const string MigrationBeforeTheMove = "20260810145852_EnforceConfigurationNotNullAndVersionDefault";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("bondstone")
        .WithUsername("bondstone")
        .WithPassword("bondstone")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Up_MovesTimeoutAndRetriesIntoTheJsonObject()
    {
        var architectureId = Guid.NewGuid();
        var sourceNodeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var configuredId = Guid.NewGuid();
        var bareId = Guid.NewGuid();

        await using (var context = NewContext())
        {
            context.GetService<IMigrator>().Migrate(MigrationBeforeTheMove);

            await SeedGraphAsync(architectureId, sourceNodeId, targetNodeId);

            await ExecuteAsync(
                """
                INSERT INTO "Connections"
                    ("Id", "ArchitectureId", "SourceNodeId", "TargetNodeId", "Protocol", "Synchronous", "TimeoutSeconds", "Retries")
                VALUES
                    (@configured, @architecture, @source, @target, 'Http', true, 5, 2),
                    (@bare,       @architecture, @source, @target, 'Http', true, NULL, NULL);
                """,
                new NpgsqlParameter("configured", configuredId),
                new NpgsqlParameter("bare", bareId),
                new NpgsqlParameter("architecture", architectureId),
                new NpgsqlParameter("source", sourceNodeId),
                new NpgsqlParameter("target", targetNodeId));
        }

        await using (var context = NewContext())
        {
            context.Database.Migrate();
        }

        var configured = await ReadConfigurationAsync(configuredId);
        var bare = await ReadConfigurationAsync(bareId);

        Assert.Contains("\"timeoutSeconds\": 5", configured);
        Assert.Contains("\"retries\": 2", configured);
        Assert.Contains("\"circuitBreaker\": false", configured);

        Assert.DoesNotContain("timeoutSeconds", bare);
        Assert.Contains("\"fallback\": false", bare);
    }

    private BondstoneDbContext NewContext() =>
        new(new DbContextOptionsBuilder<BondstoneDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    private async Task SeedGraphAsync(Guid architectureId, Guid sourceNodeId, Guid targetNodeId)
    {
        var now = DateTime.UtcNow;

        await ExecuteAsync(
            """
            INSERT INTO "Architectures" ("Id", "Name", "ShareToken", "Version", "CreatedAt", "UpdatedAt")
            VALUES (@id, 'Legacy', 'legacytokn', 1, @now, @now);
            """,
            new NpgsqlParameter("id", architectureId),
            new NpgsqlParameter("now", now));

        await ExecuteAsync(
            """
            INSERT INTO "Nodes" ("Id", "ArchitectureId", "Type", "Name", "PositionX", "PositionY", "Configuration")
            VALUES (@source, @architecture, 'Api', 'Api', 0, 0, '{}'::jsonb),
                   (@target, @architecture, 'Database', 'Db', 0, 0, '{}'::jsonb);
            """,
            new NpgsqlParameter("source", sourceNodeId),
            new NpgsqlParameter("target", targetNodeId),
            new NpgsqlParameter("architecture", architectureId));
    }

    private async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> ReadConfigurationAsync(Guid connectionId)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            @"SELECT jsonb_pretty(""Configuration"") FROM ""Connections"" WHERE ""Id"" = @id;", connection);
        command.Parameters.Add(new NpgsqlParameter("id", connectionId));

        return (string)(await command.ExecuteScalarAsync())!;
    }
}
