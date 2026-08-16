using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ResumeForge.Application.Tailoring;
using ResumeForge.Infrastructure.Persistence;
using Shouldly;
using Xunit;

namespace ResumeForge.Infrastructure.Tests.Persistence.Migrations;

/// <summary>
/// Pins the exact behaviour CONTRACTS.md §10 requires of the
/// <c>AddLearnedAtEffortToLearnedFieldMaps</c> migration: a row written under the
/// pre-<see cref="ModelEffort"/> schema must read back as <see cref="ModelEffort.Standard"/>
/// once the migration has run — not fail to parse, and not default to some other value.
/// </summary>
public sealed class AddLearnedAtEffortToLearnedFieldMapsTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AddLearnedAtEffortToLearnedFieldMapsTests() => _connection.Open();

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Existing_rows_default_to_standard_effort_after_the_migration_runs()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ResumeForgeDbContext>().UseSqlite(_connection).Options;

        // Apply every migration up to, but not including, the one under test — the exact
        // schema a row learned before ModelEffort existed would have been written under.
        await using (var pre = new ResumeForgeDbContext(options))
        {
            var migrator = pre.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync("20260816020621_ApplicationStatusSevenValueSet", ct);
        }

        await using (var seedConnectionContext = new ResumeForgeDbContext(options))
        {
            var raw = seedConnectionContext.Database.GetDbConnection();
            if (raw.State != System.Data.ConnectionState.Open)
            {
                await raw.OpenAsync(ct);
            }

            using var command = raw.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "LearnedFieldMaps" ("Host", "FormSignature", "ElementToKey", "LearnedAt", "HitCount")
                VALUES ('boards.greenhouse.io', 'sig-legacy', '{}', '2025-01-01T00:00:00+00:00', 3);
                """;
            await command.ExecuteNonQueryAsync(ct);
        }

        // Apply the rest of the migrations, including AddLearnedAtEffortToLearnedFieldMaps.
        await using var post = new ResumeForgeDbContext(options);
        await post.Database.MigrateAsync(ct);

        var effort = await post.LearnedFieldMaps
            .Where(m => m.FormSignature == "sig-legacy")
            .Select(m => m.LearnedAtEffort)
            .SingleAsync(ct);

        effort.ShouldBe(ModelEffort.Standard);
    }
}
