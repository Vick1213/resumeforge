using ResumeForge.Domain.Ids;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Application.Tailoring;

/// <summary>
/// Deterministic, pure <see cref="ICommandExecutor"/>. Per CONTRACTS.md §6: entries
/// (<c>exp:</c>, <c>prj:</c>, <c>edu:</c>, <c>cert:</c>) and skill groups carry an
/// <c>Included</c> flag that include/exclude toggles, staying in the document either way.
/// Bullets and individual skills have no such flag — excluding one removes it from its
/// parent's list; including a previously excluded one restores it from the original
/// document. Ids are assigned once by <see cref="IResumeBuilder"/> and are never
/// renumbered: excluding a bullet never shifts its siblings' ids.
/// </summary>
public sealed class CommandExecutor(TimeProvider timeProvider) : ICommandExecutor
{
    private const string RootParent = "root";

    /// <inheritdoc />
    public CommandExecutionResult Execute(ResumeDocument document, IReadOnlyList<TailorCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(commands);

        var ctx = new ExecutionContext(document);
        var diff = new List<ResumeDiffEntry>();

        foreach (var command in commands)
        {
            switch (command)
            {
                case IncludeCommand include:
                    foreach (var target in include.Targets)
                    {
                        ctx.ApplyIncludeExclude(target, include: true, include.Rationale, diff);
                    }

                    break;

                case ExcludeCommand exclude:
                    foreach (var target in exclude.Targets)
                    {
                        ctx.ApplyIncludeExclude(target, include: false, exclude.Rationale, diff);
                    }

                    break;
            }
        }

        foreach (var command in commands)
        {
            if (command is SelectVariantCommand or RewriteCommand or InjectKeywordsCommand)
            {
                ctx.ApplyVariantOrRewrite(command, diff);
            }
        }

        foreach (var command in commands)
        {
            if (command is OrderCommand order)
            {
                ctx.ApplyOrder(order, diff);
            }
        }

        foreach (var command in commands)
        {
            if (command is SetSummaryCommand setSummary)
            {
                ctx.ApplySetSummary(setSummary, diff);
            }
        }

        foreach (var command in commands)
        {
            if (command is SetTaglineCommand setTagline)
            {
                ctx.ApplySetTagline(setTagline, diff);
            }
        }

        foreach (var command in commands)
        {
            if (command is EmphasizeSkillsCommand emphasize)
            {
                ctx.ApplyEmphasizeSkills(emphasize, diff);
            }
        }

        foreach (var command in commands)
        {
            if (command is SetSectionOrderCommand setSectionOrder)
            {
                ctx.ApplySetSectionOrder(setSectionOrder, diff);
            }
        }

        return new CommandExecutionResult
        {
            Document = ctx.Build(document, timeProvider.GetUtcNow()),
            Diff = diff,
        };
    }

    /// <summary>Mutable working state for one <see cref="Execute"/> call, isolated from the input document.</summary>
    private sealed class ExecutionContext
    {
        private readonly List<ExperienceEntry> _experience;
        private readonly List<ProjectEntry> _projects;
        private readonly List<EducationEntry> _education;
        private readonly List<CertificationEntry> _certifications;
        private readonly List<SkillGroup> _skills;
        private readonly Dictionary<string, Bullet> _originalBullets;
        private readonly Dictionary<string, Skill> _originalSkills;
        private string? _summary;
        private List<SectionKind> _sectionOrder;

        public ExecutionContext(ResumeDocument document)
        {
            _experience = [.. document.Experience];
            _projects = [.. document.Projects];
            _education = [.. document.Education];
            _certifications = [.. document.Certifications];
            _skills = [.. document.Skills];
            _summary = document.Summary;
            _sectionOrder = [.. document.SectionOrder];

            _originalBullets = new Dictionary<string, Bullet>(StringComparer.Ordinal);
            foreach (var entry in document.Experience)
            {
                foreach (var bullet in entry.Bullets)
                {
                    _originalBullets[bullet.Id] = bullet;
                }
            }

            foreach (var entry in document.Projects)
            {
                foreach (var bullet in entry.Bullets)
                {
                    _originalBullets[bullet.Id] = bullet;
                }
            }

            _originalSkills = new Dictionary<string, Skill>(StringComparer.Ordinal);
            foreach (var group in document.Skills)
            {
                foreach (var skill in group.Items)
                {
                    _originalSkills[skill.Id] = skill;
                }
            }
        }

        public void ApplyIncludeExclude(string targetText, bool include, string? rationale, List<ResumeDiffEntry> diff)
        {
            if (!EntityId.TryParse(targetText, out var id))
            {
                return;
            }

            var hasFragment = id.Ordinal is not null || id.SubKey is not null;

            if (hasFragment)
            {
                if (include)
                {
                    RestoreChild(id, rationale, diff);
                }
                else
                {
                    RemoveChild(id, rationale, diff);
                }

                return;
            }

            SetEntryIncluded(id, include, rationale, diff);
        }

        private void SetEntryIncluded(EntityId id, bool included, string? rationale, List<ResumeDiffEntry> diff)
        {
            var idText = id.ToString();
            var kind = included ? DiffKind.Included : DiffKind.Excluded;

            var expIdx = _experience.FindIndex(e => e.Id == idText);
            if (expIdx >= 0)
            {
                var entry = _experience[expIdx];
                if (entry.Included != included)
                {
                    _experience[expIdx] = entry with { Included = included };
                    diff.Add(NewDiff(idText, kind, $"{entry.Role} at {entry.Organization}", null, rationale));
                }

                return;
            }

            var prjIdx = _projects.FindIndex(p => p.Id == idText);
            if (prjIdx >= 0)
            {
                var entry = _projects[prjIdx];
                if (entry.Included != included)
                {
                    _projects[prjIdx] = entry with { Included = included };
                    diff.Add(NewDiff(idText, kind, entry.Name, null, rationale));
                }

                return;
            }

            var eduIdx = _education.FindIndex(e => e.Id == idText);
            if (eduIdx >= 0)
            {
                var entry = _education[eduIdx];
                if (entry.Included != included)
                {
                    _education[eduIdx] = entry with { Included = included };
                    diff.Add(NewDiff(idText, kind, $"{entry.Credential}, {entry.Institution}", null, rationale));
                }

                return;
            }

            var certIdx = _certifications.FindIndex(c => c.Id == idText);
            if (certIdx >= 0)
            {
                var entry = _certifications[certIdx];
                if (entry.Included != included)
                {
                    _certifications[certIdx] = entry with { Included = included };
                    diff.Add(NewDiff(idText, kind, entry.Name, null, rationale));
                }

                return;
            }

            var skillIdx = _skills.FindIndex(g => g.Id == idText);
            if (skillIdx >= 0)
            {
                var group = _skills[skillIdx];
                if (group.Included != included)
                {
                    _skills[skillIdx] = group with { Included = included };
                    diff.Add(NewDiff(idText, kind, group.Label, null, rationale));
                }
            }
        }

        private void RemoveChild(EntityId id, string? rationale, List<ResumeDiffEntry> diff)
        {
            var idText = id.ToString();
            var parentText = id.Parent.ToString();

            switch (id.Kind)
            {
                case EntityKind.Experience:
                {
                    var idx = _experience.FindIndex(e => e.Id == parentText);
                    if (idx < 0)
                    {
                        return;
                    }

                    var entry = _experience[idx];
                    var bullet = entry.Bullets.FirstOrDefault(b => b.Id == idText);
                    if (bullet is null)
                    {
                        return;
                    }

                    _experience[idx] = entry with { Bullets = [.. entry.Bullets.Where(b => b.Id != idText)] };
                    diff.Add(NewDiff(idText, DiffKind.Excluded, bullet.Text, null, rationale));
                    return;
                }

                case EntityKind.Project:
                {
                    var idx = _projects.FindIndex(p => p.Id == parentText);
                    if (idx < 0)
                    {
                        return;
                    }

                    var entry = _projects[idx];
                    var bullet = entry.Bullets.FirstOrDefault(b => b.Id == idText);
                    if (bullet is null)
                    {
                        return;
                    }

                    _projects[idx] = entry with { Bullets = [.. entry.Bullets.Where(b => b.Id != idText)] };
                    diff.Add(NewDiff(idText, DiffKind.Excluded, bullet.Text, null, rationale));
                    return;
                }

                case EntityKind.SkillGroup:
                {
                    var idx = _skills.FindIndex(g => g.Id == parentText);
                    if (idx < 0)
                    {
                        return;
                    }

                    var group = _skills[idx];
                    var skill = group.Items.FirstOrDefault(s => s.Id == idText);
                    if (skill is null)
                    {
                        return;
                    }

                    _skills[idx] = group with { Items = [.. group.Items.Where(s => s.Id != idText)] };
                    diff.Add(NewDiff(idText, DiffKind.Excluded, skill.Name, null, rationale));
                    return;
                }
            }
        }

        private void RestoreChild(EntityId id, string? rationale, List<ResumeDiffEntry> diff)
        {
            var idText = id.ToString();
            var parentText = id.Parent.ToString();

            switch (id.Kind)
            {
                case EntityKind.Experience:
                {
                    var idx = _experience.FindIndex(e => e.Id == parentText);
                    if (idx < 0 || !_originalBullets.TryGetValue(idText, out var original))
                    {
                        return;
                    }

                    var entry = _experience[idx];
                    if (entry.Bullets.Any(b => b.Id == idText))
                    {
                        return;
                    }

                    var restored = entry.Bullets.Append(original).OrderBy(BulletOrdinal).ToList();
                    _experience[idx] = entry with { Bullets = restored };
                    diff.Add(NewDiff(idText, DiffKind.Included, null, original.Text, rationale));
                    return;
                }

                case EntityKind.Project:
                {
                    var idx = _projects.FindIndex(p => p.Id == parentText);
                    if (idx < 0 || !_originalBullets.TryGetValue(idText, out var original))
                    {
                        return;
                    }

                    var entry = _projects[idx];
                    if (entry.Bullets.Any(b => b.Id == idText))
                    {
                        return;
                    }

                    var restored = entry.Bullets.Append(original).OrderBy(BulletOrdinal).ToList();
                    _projects[idx] = entry with { Bullets = restored };
                    diff.Add(NewDiff(idText, DiffKind.Included, null, original.Text, rationale));
                    return;
                }

                case EntityKind.SkillGroup:
                {
                    var idx = _skills.FindIndex(g => g.Id == parentText);
                    if (idx < 0 || !_originalSkills.TryGetValue(idText, out var original))
                    {
                        return;
                    }

                    var group = _skills[idx];
                    if (group.Items.Any(s => s.Id == idText))
                    {
                        return;
                    }

                    var restored = group.Items.Append(original).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
                    _skills[idx] = group with { Items = restored };
                    diff.Add(NewDiff(idText, DiffKind.Included, null, original.Name, rationale));
                    return;
                }
            }
        }

        public void ApplyVariantOrRewrite(TailorCommand command, List<ResumeDiffEntry> diff)
        {
            switch (command)
            {
                case SelectVariantCommand sv when EntityId.TryParse(sv.Target, out var id):
                {
                    var (before, after) = UpdateBulletText(id, bullet =>
                        sv.VariantIndex >= 0 && sv.VariantIndex < bullet.Variants.Count ? bullet.Variants[sv.VariantIndex] : null);

                    if (before is not null)
                    {
                        diff.Add(NewDiff(sv.Target, DiffKind.VariantSelected, before, after, sv.Rationale));
                    }

                    break;
                }

                case RewriteCommand rw when EntityId.TryParse(rw.Target, out var id):
                {
                    var (before, after) = UpdateBulletText(id, _ => rw.Text);
                    if (before is not null)
                    {
                        diff.Add(NewDiff(rw.Target, DiffKind.Rewritten, before, after, rw.Rationale));
                    }

                    break;
                }

                case InjectKeywordsCommand inj when EntityId.TryParse(inj.Target, out var id):
                {
                    var (before, after) = UpdateBulletText(id, _ => inj.Text);
                    if (before is not null)
                    {
                        diff.Add(NewDiff(inj.Target, DiffKind.KeywordsInjected, before, after, inj.Rationale, inj.Keywords));
                    }

                    break;
                }
            }
        }

        private (string? Before, string? After) UpdateBulletText(EntityId id, Func<Bullet, string?> newText)
        {
            var idText = id.ToString();
            var parentText = id.Parent.ToString();

            if (id.Kind == EntityKind.Experience)
            {
                var idx = _experience.FindIndex(e => e.Id == parentText);
                if (idx < 0)
                {
                    return (null, null);
                }

                var entry = _experience[idx];
                var bulletIdx = entry.Bullets.ToList().FindIndex(b => b.Id == idText);
                if (bulletIdx < 0)
                {
                    return (null, null);
                }

                var bullet = entry.Bullets[bulletIdx];
                var replacement = newText(bullet);
                if (replacement is null)
                {
                    return (null, null);
                }

                var newBullets = entry.Bullets.ToList();
                newBullets[bulletIdx] = bullet with { Text = replacement };
                _experience[idx] = entry with { Bullets = newBullets };
                return (bullet.Text, replacement);
            }

            if (id.Kind == EntityKind.Project)
            {
                var idx = _projects.FindIndex(p => p.Id == parentText);
                if (idx < 0)
                {
                    return (null, null);
                }

                var entry = _projects[idx];
                var bulletIdx = entry.Bullets.ToList().FindIndex(b => b.Id == idText);
                if (bulletIdx < 0)
                {
                    return (null, null);
                }

                var bullet = entry.Bullets[bulletIdx];
                var replacement = newText(bullet);
                if (replacement is null)
                {
                    return (null, null);
                }

                var newBullets = entry.Bullets.ToList();
                newBullets[bulletIdx] = bullet with { Text = replacement };
                _projects[idx] = entry with { Bullets = newBullets };
                return (bullet.Text, replacement);
            }

            return (null, null);
        }

        public void ApplyOrder(OrderCommand order, List<ResumeDiffEntry> diff)
        {
            if (string.Equals(order.Parent, RootParent, StringComparison.Ordinal))
            {
                ApplyRootOrder(order, diff);
                return;
            }

            if (!EntityId.TryParse(order.Parent, out var parentId))
            {
                return;
            }

            switch (parentId.Kind)
            {
                case EntityKind.Experience:
                    ApplyChildBulletOrder(_experience, e => e.Id, e => e.Bullets, (e, b) => e with { Bullets = b }, parentId.ToString(), order, diff);
                    break;
                case EntityKind.Project:
                    ApplyChildBulletOrder(_projects, p => p.Id, p => p.Bullets, (p, b) => p with { Bullets = b }, parentId.ToString(), order, diff);
                    break;
                case EntityKind.SkillGroup:
                    ApplySkillOrder(parentId.ToString(), order, diff);
                    break;
            }
        }

        private void ApplyRootOrder(OrderCommand order, List<ResumeDiffEntry> diff)
        {
            if (order.Order.Count == 0 || !EntityId.TryParse(order.Order[0], out var firstId))
            {
                return;
            }

            switch (firstId.Kind)
            {
                case EntityKind.Experience:
                    ReorderList(_experience, e => e.Id, order, diff);
                    break;
                case EntityKind.Project:
                    ReorderList(_projects, p => p.Id, order, diff);
                    break;
                case EntityKind.Education:
                    ReorderList(_education, e => e.Id, order, diff);
                    break;
                case EntityKind.Certification:
                    ReorderList(_certifications, c => c.Id, order, diff);
                    break;
                case EntityKind.SkillGroup:
                    ReorderList(_skills, g => g.Id, order, diff);
                    break;
            }
        }

        private static void ReorderList<T>(List<T> list, Func<T, string> idOf, OrderCommand order, List<ResumeDiffEntry> diff)
        {
            var before = string.Join(",", list.Select(idOf));
            var reordered = ReorderByIds(list, idOf, order.Order);
            var after = string.Join(",", reordered.Select(idOf));

            list.Clear();
            list.AddRange(reordered);

            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                diff.Add(NewDiff(RootParent, DiffKind.Reordered, before, after, order.Rationale));
            }
        }

        private void ApplyChildBulletOrder<TEntry>(
            List<TEntry> list,
            Func<TEntry, string> idOf,
            Func<TEntry, IReadOnlyList<Bullet>> bulletsOf,
            Func<TEntry, IReadOnlyList<Bullet>, TEntry> withBullets,
            string parentIdText,
            OrderCommand order,
            List<ResumeDiffEntry> diff)
        {
            var idx = list.FindIndex(e => idOf(e) == parentIdText);
            if (idx < 0)
            {
                return;
            }

            var entry = list[idx];
            var bullets = bulletsOf(entry);
            var before = string.Join(",", bullets.Select(b => b.Id));
            var reordered = ReorderByIds(bullets, b => b.Id, order.Order);
            var after = string.Join(",", reordered.Select(b => b.Id));

            list[idx] = withBullets(entry, reordered);

            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                diff.Add(NewDiff(parentIdText, DiffKind.Reordered, before, after, order.Rationale));
            }
        }

        private void ApplySkillOrder(string parentIdText, OrderCommand order, List<ResumeDiffEntry> diff)
        {
            var idx = _skills.FindIndex(g => g.Id == parentIdText);
            if (idx < 0)
            {
                return;
            }

            var group = _skills[idx];
            var before = string.Join(",", group.Items.Select(s => s.Id));
            var reordered = ReorderByIds(group.Items, s => s.Id, order.Order);
            var after = string.Join(",", reordered.Select(s => s.Id));

            _skills[idx] = group with { Items = reordered };

            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                diff.Add(NewDiff(parentIdText, DiffKind.Reordered, before, after, order.Rationale));
            }
        }

        private static List<T> ReorderByIds<T>(IReadOnlyList<T> items, Func<T, string> idOf, IReadOnlyList<string> order)
        {
            var byId = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                byId.TryAdd(idOf(item), item);
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<T>(items.Count);

            foreach (var id in order)
            {
                if (byId.TryGetValue(id, out var item) && used.Add(id))
                {
                    result.Add(item);
                }
            }

            foreach (var item in items)
            {
                if (used.Add(idOf(item)))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        public void ApplySetSummary(SetSummaryCommand command, List<ResumeDiffEntry> diff)
        {
            if (string.Equals(_summary, command.Text, StringComparison.Ordinal))
            {
                return;
            }

            diff.Add(NewDiff("sum", DiffKind.SummarySet, _summary, command.Text, command.Rationale));
            _summary = command.Text;
        }

        public void ApplySetTagline(SetTaglineCommand command, List<ResumeDiffEntry> diff)
        {
            var idx = _projects.FindIndex(p => p.Id == command.Target);
            if (idx < 0)
            {
                return;
            }

            var project = _projects[idx];
            if (string.Equals(project.Tagline, command.Text, StringComparison.Ordinal))
            {
                return;
            }

            diff.Add(NewDiff(command.Target, DiffKind.TaglineSet, project.Tagline, command.Text, command.Rationale));
            _projects[idx] = project with { Tagline = command.Text };
        }

        public void ApplyEmphasizeSkills(EmphasizeSkillsCommand command, List<ResumeDiffEntry> diff)
        {
            // Skills are stored by their normalized form ("python", "cicd"); the model may
            // send them however the posting spelled them ("Python", "CI/CD"). Normalizing
            // here is what lets those match — the same fix CommandValidator's keyword
            // handling needed.
            var targets = new HashSet<string>(
                command.Skills.Select(Domain.Text.SkillNormalizer.Normalize).Where(s => s.Length > 0),
                StringComparer.Ordinal);
            if (targets.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _skills.Count; i++)
            {
                var group = _skills[i];
                var changed = false;
                var newItems = new List<Skill>(group.Items.Count);

                foreach (var skill in group.Items)
                {
                    if (targets.Contains(skill.Normalized) && !skill.Emphasized)
                    {
                        newItems.Add(skill with { Emphasized = true });
                        changed = true;
                        diff.Add(NewDiff(skill.Id, DiffKind.SkillEmphasized, null, skill.Name, command.Rationale));
                    }
                    else
                    {
                        newItems.Add(skill);
                    }
                }

                if (changed)
                {
                    _skills[i] = group with { Items = newItems };
                }
            }
        }

        public void ApplySetSectionOrder(SetSectionOrderCommand command, List<ResumeDiffEntry> diff)
        {
            var before = string.Join(",", _sectionOrder);
            var after = string.Join(",", command.Order);

            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                return;
            }

            diff.Add(NewDiff(RootParent, DiffKind.Reordered, before, after, command.Rationale));
            _sectionOrder = [.. command.Order];
        }

        public ResumeDocument Build(ResumeDocument original, DateTimeOffset now) => new()
        {
            Id = original.Id,
            Name = original.Name,
            Basics = original.Basics,
            Summary = _summary,
            Skills = _skills,
            Experience = _experience,
            Projects = _projects,
            Education = _education,
            Certifications = _certifications,
            // The model's section order is advisory; SectionOrderPolicy has the final say on
            // where education sits, because placement follows from the candidate's own career
            // stage rather than from anything in the posting. Applied here, at the one point
            // every executed document passes through, so no command path can route around it.
            SectionOrder = SectionOrderPolicy.Normalize(_sectionOrder, _education, now),
            CreatedAt = original.CreatedAt,
            UpdatedAt = now,
        };

        private static int BulletOrdinal(Bullet bullet) =>
            EntityId.TryParse(bullet.Id, out var id) && id.Ordinal is { } ordinal ? ordinal : int.MaxValue;

        private static ResumeDiffEntry NewDiff(
            string entityId, DiffKind kind, string? before, string? after, string? rationale, IReadOnlyList<string>? keywords = null) => new()
        {
            EntityId = entityId,
            Kind = kind,
            Before = before,
            After = after,
            Rationale = rationale,
            Keywords = keywords ?? [],
        };
    }
}
