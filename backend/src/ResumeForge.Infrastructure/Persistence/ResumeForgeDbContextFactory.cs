using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ResumeForge.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can construct <see cref="ResumeForgeDbContext"/>
/// without a full application host (this project is a class library with no
/// <c>Program.cs</c> of its own). Not used at runtime; the API host configures the real
/// connection string via <see cref="DependencyInjection.ServiceCollectionExtensions"/>.
/// </summary>
public sealed class ResumeForgeDbContextFactory : IDesignTimeDbContextFactory<ResumeForgeDbContext>
{
    /// <inheritdoc />
    public ResumeForgeDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ResumeForgeDbContext>();
        optionsBuilder.UseSqlite("Data Source=resumeforge.db");
        return new ResumeForgeDbContext(optionsBuilder.Options);
    }
}
