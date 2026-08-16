using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResumeForge.Infrastructure.Persistence.Migrations;

/// <summary>
/// Widens <c>ApplicationStatus</c> (<c>Applications.Status</c>, stored via
/// <c>HasConversion&lt;string&gt;()</c> as the enum member's name) from five values to the
/// seven-value closed set in CONTRACTS.md §9. The column's shape does not change — it was
/// already <c>TEXT</c> and stays <c>TEXT</c> — so this migration carries no schema
/// operation, only a data fix: the old <c>Interviewing</c> member was renamed to
/// <c>Interview</c> (a straight rename, not a split — <c>Screening</c> is a brand new
/// value with no prior data to migrate into it), so every row already stored as the string
/// "Interviewing" is rewritten to "Interview" or it would fail to parse back into the enum
/// on the next read. <c>Withdrawn</c> and every other member keep their existing name and
/// need no rewrite.
/// </summary>
public partial class ApplicationStatusSevenValueSet : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "Applications" SET "Status" = 'Interview' WHERE "Status" = 'Interviewing';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "Applications" SET "Status" = 'Interviewing' WHERE "Status" = 'Interview';
            """);
    }
}
