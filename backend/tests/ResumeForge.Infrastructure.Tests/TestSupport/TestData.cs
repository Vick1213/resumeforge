using ResumeForge.Application.Abstractions;
using ResumeForge.Application.Analysis;
using ResumeForge.Application.Graph;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Infrastructure.Tests.TestSupport;

/// <summary>Compact factory methods for building test fixtures without repeating every required property.</summary>
public static class TestData
{
    public static Bullet Bullet(string id, string text, IReadOnlyList<string>? variants = null) => new()
    {
        Id = id,
        Text = text,
        Variants = variants ?? [],
        Tags = [],
        Relevance = 0,
    };

    public static ExperienceEntry Experience(
        string id, string role, string org, DateOnly start, DateOnly? end,
        IReadOnlyList<Bullet>? bullets = null, IReadOnlyList<string>? tech = null, bool included = true, string? location = null) => new()
    {
        Id = id,
        Role = role,
        Organization = org,
        Location = location,
        StartDate = start,
        EndDate = end,
        Bullets = bullets ?? [],
        Tech = tech ?? [],
        Included = included,
    };

    public static ProjectEntry Project(
        string id, string name, DateOnly? start = null, DateOnly? end = null,
        IReadOnlyList<Bullet>? bullets = null, IReadOnlyList<string>? tech = null, bool included = true, string? tagline = null) => new()
    {
        Id = id,
        Name = name,
        Tagline = tagline,
        StartDate = start,
        EndDate = end,
        Bullets = bullets ?? [],
        Tech = tech ?? [],
        Included = included,
    };

    public static EducationEntry Education(
        string id, string institution, string credential, DateOnly? start = null, DateOnly? end = null,
        IReadOnlyList<string>? highlights = null, bool included = true) => new()
    {
        Id = id,
        Institution = institution,
        Credential = credential,
        StartDate = start,
        EndDate = end,
        Highlights = highlights ?? [],
        Included = included,
    };

    public static CertificationEntry Certification(string id, string name, string? issuer = null, bool included = true) => new()
    {
        Id = id,
        Name = name,
        Issuer = issuer,
        Included = included,
    };

    public static Skill Skill(string id, string name, string normalized, bool emphasized = false) => new()
    {
        Id = id,
        Name = name,
        Normalized = normalized,
        Emphasized = emphasized,
    };

    public static SkillGroup SkillGroup(string id, string label, IReadOnlyList<Skill> items, bool included = true) => new()
    {
        Id = id,
        Label = label,
        Items = items,
        Included = included,
    };

    public static ResumeBasics Basics(string fullName = "Jane Doe", string? headline = null, string? email = null) => new()
    {
        FullName = fullName,
        Headline = headline,
        Email = email,
    };

    public static ResumeDocument Document(
        string id = "doc-1",
        string name = "Base resume",
        ResumeBasics? basics = null,
        IReadOnlyList<ExperienceEntry>? experience = null,
        IReadOnlyList<ProjectEntry>? projects = null,
        IReadOnlyList<EducationEntry>? education = null,
        IReadOnlyList<CertificationEntry>? certifications = null,
        IReadOnlyList<SkillGroup>? skills = null,
        string? summary = null,
        IReadOnlyList<SectionKind>? sectionOrder = null,
        DateTimeOffset createdAt = default,
        DateTimeOffset updatedAt = default) => new()
    {
        Id = id,
        Name = name,
        Basics = basics ?? Basics(),
        Summary = summary,
        Skills = skills ?? [],
        Experience = experience ?? [],
        Projects = projects ?? [],
        Education = education ?? [],
        Certifications = certifications ?? [],
        SectionOrder = sectionOrder ??
        [
            SectionKind.Summary, SectionKind.Skills, SectionKind.Experience,
            SectionKind.Projects, SectionKind.Education, SectionKind.Certifications,
        ],
        CreatedAt = createdAt,
        UpdatedAt = updatedAt,
    };

    public static KnowledgeItem KnowledgeItem(
        KnowledgeItemType type,
        string slug,
        string title,
        string? organization = null,
        DateOnly? start = null,
        DateOnly? end = null,
        bool isCurrent = false,
        IReadOnlyList<string>? tech = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<KnowledgeBullet>? bullets = null,
        IReadOnlyDictionary<string, string>? extra = null,
        KnowledgeSource source = KnowledgeSource.Manual)
    {
        var prefix = type switch
        {
            KnowledgeItemType.Experience => "exp",
            KnowledgeItemType.Project => "prj",
            KnowledgeItemType.Education => "edu",
            KnowledgeItemType.Certification => "cert",
            _ => "exp",
        };

        return new KnowledgeItem
        {
            Id = EntityId.Parse($"{prefix}:{slug}"),
            Type = type,
            Slug = slug,
            FilePath = $"profile/{slug}.md",
            Title = title,
            Organization = organization,
            StartDate = start,
            EndDate = end,
            IsCurrent = isCurrent,
            Tech = tech ?? [],
            Tags = tags ?? [],
            Bullets = bullets ?? [],
            Extra = extra ?? new Dictionary<string, string>(),
            Source = source,
            RawMarkdown = string.Empty,
        };
    }

    public static KnowledgeBullet KnowledgeBullet(string text, IReadOnlyList<string>? variants = null) => new()
    {
        Text = text,
        Variants = variants ?? [],
        Tags = [],
    };

    public static JobPosting Posting(
        string id = "job-1", string sourceUrl = "https://example.com/job", string rawText = "posting text",
        string? title = null, string? company = null, string? location = null, DateTimeOffset fetchedAt = default) => new()
    {
        Id = id,
        SourceUrl = sourceUrl,
        Title = title,
        Company = company,
        Location = location,
        RawText = rawText,
        FetchedAt = fetchedAt,
    };

    public static Requirement Requirement(string id, string text, bool mandatory = true) => new()
    {
        Id = id,
        Text = text,
        Kind = RequirementKind.Skill,
        IsMandatory = mandatory,
        Skills = [],
        Weight = 0.5,
    };

    public static JobAnalysis Analysis(
        string jobId = "job-1",
        IReadOnlyList<Requirement>? requirements = null,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<string>? matchedSkills = null,
        IReadOnlyList<string>? missingSkills = null,
        SeniorityLevel seniority = SeniorityLevel.Mid) => new()
    {
        JobId = jobId,
        Requirements = requirements ?? [],
        Keywords = keywords ?? [],
        MatchedSkills = matchedSkills ?? [],
        MissingSkills = missingSkills ?? [],
        Seniority = seniority,
    };

    public static TailoringResult TailoringResult(ResumeDocument document, IReadOnlyList<GraphNodeTrace>? trace = null) => new()
    {
        Document = document,
        Diff = [],
        Commands = new CommandValidationResult { Accepted = [], Rejected = [] },
        Coverage = new CoverageReport { Score = 1.0, Requirements = [] },
        Usage = TokenUsage.Empty,
        Trace = trace ?? [],
    };

    public static GraphNodeTrace Trace(string node, GraphNodeStatus status = GraphNodeStatus.Succeeded) => new()
    {
        Node = node,
        Status = status,
        Duration = TimeSpan.FromMilliseconds(5),
    };
}
