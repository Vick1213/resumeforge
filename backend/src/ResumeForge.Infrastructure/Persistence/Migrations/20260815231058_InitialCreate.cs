using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResumeForge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Applications",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                JobId = table.Column<string>(type: "TEXT", nullable: false),
                ResumeId = table.Column<string>(type: "TEXT", nullable: true),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                Notes = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Applications", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "JobAnalyses",
            columns: table => new
            {
                JobId = table.Column<string>(type: "TEXT", nullable: false),
                Requirements = table.Column<string>(type: "TEXT", nullable: false),
                Keywords = table.Column<string>(type: "TEXT", nullable: false),
                MatchedSkills = table.Column<string>(type: "TEXT", nullable: false),
                MissingSkills = table.Column<string>(type: "TEXT", nullable: false),
                Seniority = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobAnalyses", x => x.JobId);
            });

        migrationBuilder.CreateTable(
            name: "JobPostings",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                Company = table.Column<string>(type: "TEXT", nullable: true),
                Title = table.Column<string>(type: "TEXT", nullable: true),
                Location = table.Column<string>(type: "TEXT", nullable: true),
                RawText = table.Column<string>(type: "TEXT", nullable: false),
                FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JobPostings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "LearnedFieldMaps",
            columns: table => new
            {
                Host = table.Column<string>(type: "TEXT", nullable: false),
                FormSignature = table.Column<string>(type: "TEXT", nullable: false),
                ElementToKey = table.Column<string>(type: "TEXT", nullable: false),
                LearnedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                HitCount = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LearnedFieldMaps", x => new { x.Host, x.FormSignature });
            });

        migrationBuilder.CreateTable(
            name: "ModelCacheEntries",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", nullable: false),
                ModelId = table.Column<string>(type: "TEXT", nullable: false),
                SchemaName = table.Column<string>(type: "TEXT", nullable: false),
                ResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ModelCacheEntries", x => x.Key);
            });

        migrationBuilder.CreateTable(
            name: "Resumes",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Basics = table.Column<string>(type: "TEXT", nullable: false),
                Summary = table.Column<string>(type: "TEXT", nullable: true),
                Skills = table.Column<string>(type: "TEXT", nullable: false),
                Experience = table.Column<string>(type: "TEXT", nullable: false),
                Projects = table.Column<string>(type: "TEXT", nullable: false),
                Education = table.Column<string>(type: "TEXT", nullable: false),
                Certifications = table.Column<string>(type: "TEXT", nullable: false),
                SectionOrder = table.Column<string>(type: "TEXT", nullable: false),
                IsBase = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Resumes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TailoringRuns",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                JobId = table.Column<string>(type: "TEXT", nullable: false),
                BaseResumeId = table.Column<string>(type: "TEXT", nullable: true),
                Result = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TailoringRuns", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Applications_JobId",
            table: "Applications",
            column: "JobId");

        migrationBuilder.CreateIndex(
            name: "IX_Applications_UpdatedAt",
            table: "Applications",
            column: "UpdatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_Resumes_IsBase",
            table: "Resumes",
            column: "IsBase");

        migrationBuilder.CreateIndex(
            name: "IX_Resumes_UpdatedAt",
            table: "Resumes",
            column: "UpdatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_TailoringRuns_CreatedAt",
            table: "TailoringRuns",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_TailoringRuns_JobId",
            table: "TailoringRuns",
            column: "JobId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Applications");

        migrationBuilder.DropTable(
            name: "JobAnalyses");

        migrationBuilder.DropTable(
            name: "JobPostings");

        migrationBuilder.DropTable(
            name: "LearnedFieldMaps");

        migrationBuilder.DropTable(
            name: "ModelCacheEntries");

        migrationBuilder.DropTable(
            name: "Resumes");

        migrationBuilder.DropTable(
            name: "TailoringRuns");
    }
}
