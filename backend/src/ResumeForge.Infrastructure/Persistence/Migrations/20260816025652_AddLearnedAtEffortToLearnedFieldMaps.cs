using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResumeForge.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds <see cref="Entities.LearnedFieldMapEntity.LearnedAtEffort"/> (CONTRACTS.md §10),
/// stored via <c>HasConversion&lt;string&gt;()</c> as the enum member's name, matching
/// every other enum column in this schema. Every row that already exists was necessarily
/// learned before <c>ModelEffort</c> existed, i.e. under what is now the
/// <see cref="ResumeForge.Application.Tailoring.ModelEffort.Standard"/> behaviour, so the
/// new column defaults existing rows to <c>"Standard"</c> rather than an empty string.
/// </summary>
public partial class AddLearnedAtEffortToLearnedFieldMaps : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LearnedAtEffort",
            table: "LearnedFieldMaps",
            type: "TEXT",
            nullable: false,
            defaultValue: "Standard");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LearnedAtEffort",
            table: "LearnedFieldMaps");
    }
}
