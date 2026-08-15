using Microsoft.Extensions.Logging;
using ResumeForge.Application.Abstractions;
using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Knowledge;
using ResumeForge.Domain.Resume;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ResumeForge.Infrastructure.Knowledge;

/// <summary>
/// Filesystem <see cref="IKnowledgeBaseReader"/> for the markdown knowledge base described
/// in CONTRACTS.md §3. Frontmatter is parsed with YamlDotNet's low-level representation
/// model (to preserve key order and source line numbers for diagnostics); bullet and
/// variant text is extracted by direct line scanning of the body so writing it back is
/// exactly predictable. A malformed file is recorded as a <see cref="KnowledgeBaseDiagnostic"/>
/// and skipped; it never aborts the rest of the load.
/// </summary>
public sealed class MarkdownKnowledgeBaseReader(
    IProfileRootProvider profileRootProvider,
    ILogger<MarkdownKnowledgeBaseReader> logger) : IKnowledgeBaseReader
{
    private static readonly (string Directory, KnowledgeItemType Type)[] Categories =
    [
        ("experience", KnowledgeItemType.Experience),
        ("projects", KnowledgeItemType.Project),
        ("education", KnowledgeItemType.Education),
        ("certifications", KnowledgeItemType.Certification),
    ];

    private enum BodyMode { BulletsWithVariants, HighlightsOnly, Ignored }

    private readonly record struct TypeSpec(
        string TitleKey,
        string? OrganizationKey,
        string StartDateKey,
        string? EndDateKey,
        bool SupportsTechTags,
        bool SupportsSource,
        BodyMode BodyMode);

    private static readonly Dictionary<KnowledgeItemType, TypeSpec> Specs = new()
    {
        [KnowledgeItemType.Experience] = new TypeSpec("role", "organization", "startDate", "endDate", true, false, BodyMode.BulletsWithVariants),
        [KnowledgeItemType.Project] = new TypeSpec("name", null, "startDate", "endDate", true, true, BodyMode.BulletsWithVariants),
        [KnowledgeItemType.Education] = new TypeSpec("credential", "institution", "startDate", "endDate", false, false, BodyMode.HighlightsOnly),
        [KnowledgeItemType.Certification] = new TypeSpec("name", "issuer", "issuedOn", null, false, false, BodyMode.Ignored),
    };

    private sealed record RawField(YamlNode Value, int Line);

    /// <inheritdoc />
    public async Task<KnowledgeBaseSnapshot> ReadAsync(CancellationToken ct)
    {
        var root = profileRootProvider.ProfileRoot;
        var items = new List<KnowledgeItem>();
        var diagnostics = new List<KnowledgeBaseDiagnostic>();

        if (!Directory.Exists(root))
        {
            logger.LogWarning("Profile root '{ProfileRoot}' does not exist; the knowledge base will load empty.", root);
        }

        foreach (var (dirName, type) in Categories)
        {
            var dir = Path.Combine(root, dirName);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.md").OrderBy(f => f, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                string text;
                try
                {
                    text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    diagnostics.Add(Diag(relativePath, 1, $"Could not read file: {ex.Message}", DiagnosticSeverity.Error));
                    continue;
                }

                try
                {
                    var item = TryParseItem(relativePath, file, type, text, diagnostics);
                    if (item is not null)
                    {
                        items.Add(item);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    diagnostics.Add(Diag(relativePath, 1, $"Unexpected error parsing file: {ex.Message}", DiagnosticSeverity.Error));
                }
            }
        }

        var (basics, summary) = await ReadBasicsAsync(root, diagnostics, ct).ConfigureAwait(false);

        return new KnowledgeBaseSnapshot
        {
            Items = items,
            Basics = basics,
            DefaultSummary = summary,
            Diagnostics = diagnostics,
        };
    }

    private KnowledgeItem? TryParseItem(
        string relativePath, string absolutePath, KnowledgeItemType directoryType, string text, List<KnowledgeBaseDiagnostic> diagnostics)
    {
        if (!TrySplitFrontmatter(text, out var frontmatterYaml, out var frontmatterEndLine, out var bodyLines))
        {
            diagnostics.Add(Diag(relativePath, 1, "File must start with a '---' frontmatter block terminated by another '---' line.", DiagnosticSeverity.Error));
            return null;
        }

        if (!TryParseMapping(frontmatterYaml, relativePath, diagnostics, out var mapping))
        {
            return null;
        }

        var fields = ToOrderedFields(mapping);

        var slug = Path.GetFileNameWithoutExtension(absolutePath);
        var prefix = KindPrefix(directoryType);
        if (!EntityId.TryParse($"{prefix}:{slug}", out var id))
        {
            diagnostics.Add(Diag(relativePath, 1, $"Filename '{Path.GetFileName(absolutePath)}' does not produce a valid slug.", DiagnosticSeverity.Error));
            return null;
        }

        var resolvedType = directoryType;
        if (fields.Remove("type", out var typeField))
        {
            var literal = GetScalar(typeField.Value);
            if (!KnowledgeMarkdownFormat.TryParseTypeLiteral(literal, out var parsedType))
            {
                diagnostics.Add(Diag(relativePath, typeField.Line, $"Unrecognized 'type: {literal}'; using '{KnowledgeMarkdownFormat.TypeLiteral(directoryType)}' from its directory.", DiagnosticSeverity.Warning));
            }
            else if (parsedType != directoryType)
            {
                diagnostics.Add(Diag(relativePath, typeField.Line, $"'type: {literal}' does not match its containing directory; using '{KnowledgeMarkdownFormat.TypeLiteral(directoryType)}'.", DiagnosticSeverity.Warning));
            }
        }

        var spec = Specs[resolvedType];

        if (!fields.Remove(spec.TitleKey, out var titleField) || string.IsNullOrWhiteSpace(GetScalar(titleField.Value)))
        {
            diagnostics.Add(Diag(relativePath, frontmatterEndLine, $"Missing required '{spec.TitleKey}' field.", DiagnosticSeverity.Error));
            return null;
        }

        var title = GetScalar(titleField.Value)!.Trim();

        string? organization = null;
        if (spec.OrganizationKey is not null && fields.Remove(spec.OrganizationKey, out var orgField))
        {
            organization = GetScalar(orgField.Value);
        }

        if (!TryParseDateField(fields, spec.StartDateKey, relativePath, diagnostics, out var startDate, out var startIsCurrent))
        {
            return null;
        }

        DateOnly? endDate = null;
        var isCurrent = false;
        if (spec.EndDateKey is not null)
        {
            if (!TryParseDateField(fields, spec.EndDateKey, relativePath, diagnostics, out endDate, out isCurrent))
            {
                return null;
            }
        }

        _ = startIsCurrent; // "startDate: present" has no defined meaning; the value is parsed but not applied.

        IReadOnlyList<string> tech = [];
        IReadOnlyList<string> tags = [];
        if (spec.SupportsTechTags)
        {
            tech = ExtractListField(fields, "tech", relativePath, diagnostics);
            tags = ExtractListField(fields, "tags", relativePath, diagnostics);
        }

        var source = KnowledgeSource.Manual;
        if (spec.SupportsSource && fields.Remove("source", out var sourceField))
        {
            var literal = GetScalar(sourceField.Value)?.Trim().ToLowerInvariant();
            source = literal switch
            {
                "manual" => KnowledgeSource.Manual,
                "github" => KnowledgeSource.GitHub,
                _ => WarnUnrecognizedSource(relativePath, sourceField.Line, literal, diagnostics),
            };
        }

        var extra = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, field) in fields)
        {
            extra[key] = ScalarizeForExtra(field.Value);
        }

        var bullets = ParseBody(spec.BodyMode, bodyLines, frontmatterEndLine, relativePath, diagnostics);

        return new KnowledgeItem
        {
            Id = id,
            Type = resolvedType,
            Slug = slug,
            FilePath = relativePath,
            Title = title,
            Organization = organization,
            StartDate = startDate,
            EndDate = endDate,
            IsCurrent = isCurrent,
            Tech = tech,
            Tags = tags,
            Bullets = bullets,
            Extra = extra,
            Source = source,
            RawMarkdown = text,
        };
    }

    private static KnowledgeSource WarnUnrecognizedSource(string relativePath, int line, string? literal, List<KnowledgeBaseDiagnostic> diagnostics)
    {
        diagnostics.Add(Diag(relativePath, line, $"Unrecognized 'source: {literal}'; treating as 'manual'.", DiagnosticSeverity.Warning));
        return KnowledgeSource.Manual;
    }

    private static bool TryParseDateField(
        OrderedDictionary<string, RawField> fields, string key, string relativePath,
        List<KnowledgeBaseDiagnostic> diagnostics, out DateOnly? value, out bool isCurrent)
    {
        value = null;
        isCurrent = false;

        if (!fields.Remove(key, out var field))
        {
            return true;
        }

        var raw = GetScalar(field.Value);
        if (raw is null)
        {
            diagnostics.Add(Diag(relativePath, field.Line, $"'{key}' must be a plain date value.", DiagnosticSeverity.Error));
            return false;
        }

        if (!KnowledgeDateValue.TryParse(raw, out value, out isCurrent))
        {
            diagnostics.Add(Diag(relativePath, field.Line, $"'{key}: {raw}' is not a valid date; expected yyyy-MM-dd, yyyy-MM, yyyy, or 'present'.", DiagnosticSeverity.Error));
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> ExtractListField(
        OrderedDictionary<string, RawField> fields, string key, string relativePath, List<KnowledgeBaseDiagnostic> diagnostics)
    {
        if (!fields.Remove(key, out var field))
        {
            return [];
        }

        var sequence = GetSequence(field.Value);
        if (sequence is null)
        {
            diagnostics.Add(Diag(relativePath, field.Line, $"'{key}' must be an inline list, e.g. '{key}: [A, B]'.", DiagnosticSeverity.Warning));
            return [];
        }

        return sequence;
    }

    private static IReadOnlyList<KnowledgeBullet> ParseBody(
        BodyMode mode, IReadOnlyList<string> bodyLines, int frontmatterEndLine, string relativePath, List<KnowledgeBaseDiagnostic> diagnostics)
    {
        if (mode == BodyMode.Ignored)
        {
            return [];
        }

        var bullets = new List<(string Text, List<string> Variants)>();

        for (var i = 0; i < bodyLines.Count; i++)
        {
            var line = bodyLines[i];
            if (line.Length == 0)
            {
                continue;
            }

            var trimmed = line.TrimStart(' ');
            var indent = line.Length - trimmed.Length;

            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                diagnostics.Add(Diag(relativePath, frontmatterEndLine + 1 + i, "Ignoring body content that is not a '-' list item.", DiagnosticSeverity.Warning));
                continue;
            }

            var text = trimmed[2..].Trim();

            if (indent == 0)
            {
                bullets.Add((text, []));
                continue;
            }

            if (mode == BodyMode.HighlightsOnly)
            {
                diagnostics.Add(Diag(relativePath, frontmatterEndLine + 1 + i, "Nested list items are not supported for this entry type and were ignored.", DiagnosticSeverity.Warning));
                continue;
            }

            if (bullets.Count == 0)
            {
                diagnostics.Add(Diag(relativePath, frontmatterEndLine + 1 + i, "Indented list item has no preceding bullet to attach to; ignored.", DiagnosticSeverity.Warning));
                continue;
            }

            bullets[^1].Variants.Add(text);
        }

        return [.. bullets.Select(b => new KnowledgeBullet { Text = b.Text, Variants = b.Variants, Tags = [] })];
    }

    private async Task<(ResumeBasics Basics, string? Summary)> ReadBasicsAsync(
        string root, List<KnowledgeBaseDiagnostic> diagnostics, CancellationToken ct)
    {
        var path = Path.Combine(root, "basics.md");

        if (!File.Exists(path))
        {
            diagnostics.Add(Diag("basics.md", 1, "profile/basics.md was not found; using empty basics.", DiagnosticSeverity.Warning));
            return (new ResumeBasics { FullName = string.Empty }, null);
        }

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

        if (!TrySplitFrontmatter(text, out var frontmatterYaml, out _, out var bodyLines))
        {
            diagnostics.Add(Diag("basics.md", 1, "basics.md must start with a '---' frontmatter block terminated by another '---' line.", DiagnosticSeverity.Error));
            return (new ResumeBasics { FullName = string.Empty }, null);
        }

        if (!TryParseMapping(frontmatterYaml, "basics.md", diagnostics, out var mapping))
        {
            return (new ResumeBasics { FullName = string.Empty }, null);
        }

        var fields = ToOrderedFields(mapping);

        string? Get(string key) => fields.TryGetValue(key, out var f) ? GetScalar(f.Value) : null;

        var fullName = Get("fullName");
        if (string.IsNullOrWhiteSpace(fullName))
        {
            diagnostics.Add(Diag("basics.md", 1, "Missing required 'fullName' field.", DiagnosticSeverity.Error));
            fullName = string.Empty;
        }

        var basics = new ResumeBasics
        {
            FullName = fullName,
            Headline = Get("headline"),
            Email = Get("email"),
            Phone = Get("phone"),
            Location = Get("location"),
            Website = Get("website"),
            LinkedIn = Get("linkedin"),
            GitHub = Get("github"),
        };

        var summaryLines = bodyLines.Skip(1).ToList();
        while (summaryLines.Count > 0 && summaryLines[^1].Length == 0)
        {
            summaryLines.RemoveAt(summaryLines.Count - 1);
        }

        var summary = summaryLines.Count > 0 ? string.Join('\n', summaryLines) : null;

        return (basics, summary);
    }

    private static bool TryParseMapping(string frontmatterYaml, string relativePath, List<KnowledgeBaseDiagnostic> diagnostics, out YamlMappingNode mapping)
    {
        mapping = null!;

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(frontmatterYaml));

            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                diagnostics.Add(Diag(relativePath, 2, "Frontmatter must be a YAML mapping.", DiagnosticSeverity.Error));
                return false;
            }

            mapping = root;
            return true;
        }
        catch (YamlException ex)
        {
            diagnostics.Add(Diag(relativePath, (int)ex.Start.Line + 2, $"Invalid YAML frontmatter: {ex.Message}", DiagnosticSeverity.Error));
            return false;
        }
    }

    private static OrderedDictionary<string, RawField> ToOrderedFields(YamlMappingNode mapping)
    {
        var result = new OrderedDictionary<string, RawField>(StringComparer.Ordinal);

        foreach (var pair in mapping.Children)
        {
            if (pair.Key is YamlScalarNode { Value: { } key })
            {
                result[key] = new RawField(pair.Value, (int)pair.Key.Start.Line + 2);
            }
        }

        return result;
    }

    private static string? GetScalar(YamlNode node) => node is YamlScalarNode { Value: { } value } ? value : null;

    private static IReadOnlyList<string>? GetSequence(YamlNode node) =>
        node is YamlSequenceNode seq
            ? [.. seq.Children.OfType<YamlScalarNode>().Select(c => (c.Value ?? string.Empty).Trim())]
            : null;

    private static string ScalarizeForExtra(YamlNode node) => node switch
    {
        YamlScalarNode { Value: { } value } => value,
        YamlSequenceNode seq => KnowledgeMarkdownFormat.FormatInlineList(
            [.. seq.Children.OfType<YamlScalarNode>().Select(c => (c.Value ?? string.Empty).Trim())]),
        _ => string.Empty,
    };

    private static string KindPrefix(KnowledgeItemType type) => type switch
    {
        KnowledgeItemType.Experience => "exp",
        KnowledgeItemType.Project => "prj",
        KnowledgeItemType.Education => "edu",
        KnowledgeItemType.Certification => "cert",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported knowledge item type."),
    };

    /// <summary>
    /// Splits <paramref name="text"/> into its frontmatter YAML and body lines. The body is
    /// every line after the closing fence, including the separating blank line, so callers
    /// can distinguish "no body" (certifications) from "blank line then content".
    /// </summary>
    private static bool TrySplitFrontmatter(string text, out string frontmatterYaml, out int frontmatterEndLine, out IReadOnlyList<string> bodyLines)
    {
        frontmatterYaml = string.Empty;
        frontmatterEndLine = 0;
        bodyLines = [];

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0] != "---")
        {
            return false;
        }

        var closeIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i] == "---")
            {
                closeIndex = i;
                break;
            }
        }

        if (closeIndex < 0)
        {
            return false;
        }

        frontmatterYaml = string.Join('\n', lines[1..closeIndex]);
        frontmatterEndLine = closeIndex + 1;
        bodyLines = lines[(closeIndex + 1)..];
        return true;
    }

    private static KnowledgeBaseDiagnostic Diag(string filePath, int line, string message, DiagnosticSeverity severity) => new()
    {
        FilePath = filePath,
        Line = line,
        Message = message,
        Severity = severity,
    };
}
