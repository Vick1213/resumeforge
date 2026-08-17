using ResumeForge.Application.Analysis;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tests.TestSupport;

/// <summary>Compact factory methods for building test fixtures without repeating every required property.</summary>
public static class TestData
{
    public static Bullet Bullet(
        string id, string text, IReadOnlyList<string>? variants = null, IReadOnlyList<string>? tags = null, double relevance = 0) => new()
    {
        Id = id,
        Text = text,
        Variants = variants ?? [],
        Tags = tags ?? [],
        Relevance = relevance,
    };

    public static ExperienceEntry Experience(
        string id, string role, string org, DateOnly start, DateOnly? end,
        IReadOnlyList<Bullet>? bullets = null, IReadOnlyList<string>? tech = null, bool included = true) => new()
    {
        Id = id,
        Role = role,
        Organization = org,
        StartDate = start,
        EndDate = end,
        Bullets = bullets ?? [],
        Tech = tech ?? [],
        Included = included,
    };

    public static ProjectEntry Project(
        string id, string name, DateOnly? start = null, DateOnly? end = null,
        IReadOnlyList<Bullet>? bullets = null, IReadOnlyList<string>? tech = null, bool included = true,
        string? tagline = null) => new()
    {
        Id = id,
        Name = name,
        StartDate = start,
        EndDate = end,
        Bullets = bullets ?? [],
        Tech = tech ?? [],
        Included = included,
        Tagline = tagline,
    };

    public static EducationEntry Education(
        string id, string institution, string credential, DateOnly? start = null, DateOnly? end = null, bool included = true) => new()
    {
        Id = id,
        Institution = institution,
        Credential = credential,
        StartDate = start,
        EndDate = end,
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

    public static ResumeBasics Basics(string fullName = "Jane Doe") => new() { FullName = fullName };

    public static ResumeDocument Document(
        string id = "doc-1",
        string name = "Base resume",
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
        Basics = Basics(),
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

    public static Requirement Requirement(
        string id, string text, RequirementKind kind, bool mandatory, IReadOnlyList<string>? skills = null, double weight = 0.5) => new()
    {
        Id = id,
        Text = text,
        Kind = kind,
        IsMandatory = mandatory,
        Skills = skills ?? [],
        Weight = weight,
    };

    public static JobAnalysis Analysis(
        string jobId = "job-1",
        IReadOnlyList<Requirement>? requirements = null,
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<string>? matchedSkills = null,
        IReadOnlyList<string>? missingSkills = null,
        SeniorityLevel seniority = SeniorityLevel.Unknown) => new()
    {
        JobId = jobId,
        Requirements = requirements ?? [],
        Keywords = keywords ?? [],
        MatchedSkills = matchedSkills ?? [],
        MissingSkills = missingSkills ?? [],
        Seniority = seniority,
    };

    public static JobPosting Posting(
        string id = "job-1", string rawText = "", string? title = null, string? company = null, DateTimeOffset fetchedAt = default) => new()
    {
        Id = id,
        SourceUrl = "https://example.com/job",
        Title = title,
        Company = company,
        RawText = rawText,
        FetchedAt = fetchedAt,
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
        IReadOnlyList<KnowledgeBullet>? bullets = null,
        IReadOnlyDictionary<string, string>? extra = null)
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
            Id = ResumeForge.Domain.Ids.EntityId.Parse($"{prefix}:{slug}"),
            Type = type,
            Slug = slug,
            FilePath = $"profile/{slug}.md",
            Title = title,
            Organization = organization,
            StartDate = start,
            EndDate = end,
            IsCurrent = isCurrent,
            Tech = tech ?? [],
            Bullets = bullets ?? [],
            Extra = extra ?? new Dictionary<string, string>(),
            Source = KnowledgeSource.Manual,
            RawMarkdown = string.Empty,
        };
    }

    public static KnowledgeBullet KnowledgeBullet(string text, IReadOnlyList<string>? variants = null, IReadOnlyList<string>? tags = null) => new()
    {
        Text = text,
        Variants = variants ?? [],
        Tags = tags ?? [],
    };
}
