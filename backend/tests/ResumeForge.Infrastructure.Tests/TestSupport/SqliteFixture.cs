using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ResumeForge.Infrastructure.Persistence;

namespace ResumeForge.Infrastructure.Tests.TestSupport;

/// <summary>
/// A <see cref="ResumeForgeDbContext"/> backed by a kept-open in-memory SQLite connection,
/// with the real EF migration applied — so repository tests exercise the actual schema,
/// not just <c>EnsureCreated</c>. Dispose closes the connection, which destroys the
/// in-memory database.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Opens a fresh in-memory database and applies every migration to it.</summary>
    public SqliteFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ResumeForgeDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new ResumeForgeDbContext(options);
        Context.Database.Migrate();
    }

    /// <summary>The live context. Callers may dispose and recreate contexts against the same connection to force a reload.</summary>
    public ResumeForgeDbContext Context { get; private set; }

    /// <summary>
    /// Disposes the current context and opens a fresh one against the same connection, so a
    /// test can assert what was actually persisted rather than reading back its own tracked
    /// entity instances.
    /// </summary>
    public ResumeForgeDbContext Reload()
    {
        Context.Dispose();

        var options = new DbContextOptionsBuilder<ResumeForgeDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new ResumeForgeDbContext(options);
        return Context;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
