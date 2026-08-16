using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResumeForge.Infrastructure.Persistence.Migrations;

/// <summary>
/// Makes base-resume identity an explicit, enforced-unique flag instead of a name match.
/// <see cref="Entities.ResumeEntity.IsBase"/> already existed as a real column — populated
/// at save time by comparing <c>ResumeDocument.Name</c> against the literal "Base resume"
/// — but nothing stopped two rows from both carrying <c>IsBase = 1</c> (e.g. two resumes
/// both literally named "Base resume" before this fix), and nothing re-derived the flag
/// for a row saved directly against the column. This migration backfills/dedupes existing
/// data first, then adds a unique filtered index so the database itself enforces "at most
/// one base resume", matching <c>ResumeRepository.SaveAsync</c>'s new explicit-flag
/// contract.
/// </summary>
public partial class ResumeIsBaseUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Backfill: flag any pre-existing row whose Name still matches the old literal
        // heuristic but whose IsBase column was never set (e.g. written before that
        // save-time logic existed).
        migrationBuilder.Sql(
            """
            UPDATE "Resumes" SET "IsBase" = 1 WHERE "Name" = 'Base resume' AND "IsBase" = 0;
            """);

        // Dedupe: the unique index below requires at most one IsBase = 1 row to already
        // exist. Keep the most recently updated flagged row (the one GetBaseAsync would
        // have picked under the old "most recent wins" tie-break) and clear the rest.
        migrationBuilder.Sql(
            """
            UPDATE "Resumes"
            SET "IsBase" = 0
            WHERE "IsBase" = 1
              AND "Id" <> (
                SELECT "Id" FROM "Resumes" WHERE "IsBase" = 1 ORDER BY "UpdatedAt" DESC, "Id" LIMIT 1
              );
            """);

        migrationBuilder.DropIndex(
            name: "IX_Resumes_IsBase",
            table: "Resumes");

        migrationBuilder.CreateIndex(
            name: "IX_Resumes_IsBase",
            table: "Resumes",
            column: "IsBase",
            unique: true,
            filter: "\"IsBase\" = 1");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Resumes_IsBase",
            table: "Resumes");

        migrationBuilder.CreateIndex(
            name: "IX_Resumes_IsBase",
            table: "Resumes",
            column: "IsBase");
    }
}
