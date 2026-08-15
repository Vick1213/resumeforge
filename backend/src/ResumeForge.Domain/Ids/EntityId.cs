using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;

namespace ResumeForge.Domain.Ids;

/// <summary>
/// The kind of node an <see cref="EntityId"/> addresses, and the serialized prefix used
/// for each in the wire form of the id (<c>exp</c>, <c>prj</c>, <c>edu</c>, <c>skl</c>,
/// <c>cert</c>, <c>sum</c>).
/// </summary>
public enum EntityKind
{
    /// <summary>An experience entry (prefix <c>exp</c>).</summary>
    Experience,

    /// <summary>A project entry (prefix <c>prj</c>).</summary>
    Project,

    /// <summary>An education entry (prefix <c>edu</c>).</summary>
    Education,

    /// <summary>A skill group (prefix <c>skl</c>).</summary>
    SkillGroup,

    /// <summary>A certification entry (prefix <c>cert</c>).</summary>
    Certification,

    /// <summary>The single summary block (prefix <c>sum</c>, no slug).</summary>
    Summary,
}

/// <summary>
/// A stable, addressable identifier for a node in a resume or knowledge base document.
/// The wire form is <c>kind:slug[#fragment]</c> (e.g. <c>exp:acme-corp#2</c>,
/// <c>skl:languages#csharp</c>), or the bare literal <c>sum</c> for the summary block,
/// which has no slug. IDs are case-sensitive and round-trip exactly through
/// <see cref="ToString"/> and <see cref="Parse(string)"/>.
/// </summary>
[JsonConverter(typeof(EntityIdJsonConverter))]
public readonly record struct EntityId : IParsable<EntityId>
{
    private EntityId(EntityKind kind, string slug, int? ordinal, string? subKey)
    {
        Kind = kind;
        Slug = slug;
        Ordinal = ordinal;
        SubKey = subKey;
    }

    /// <summary>The kind of node this id addresses.</summary>
    public EntityKind Kind { get; }

    /// <summary>
    /// The lowercase <c>[a-z0-9-]+</c> slug identifying the parent entity. Empty for
    /// <see cref="EntityKind.Summary"/>, which has no slug.
    /// </summary>
    public string Slug { get; }

    /// <summary>
    /// The numeric ordinal carried by a <c>#</c> fragment, when present. A fragment sets
    /// this property rather than <see cref="SubKey"/> when it consists entirely of
    /// digits — the form used to address a bullet within an experience or project entry
    /// (e.g. the <c>2</c> in <c>exp:acme-corp#2</c>). Mutually exclusive with
    /// <see cref="SubKey"/>.
    /// </summary>
    public int? Ordinal { get; }

    /// <summary>
    /// The named sub-key carried by a <c>#</c> fragment, when present. A fragment sets
    /// this property rather than <see cref="Ordinal"/> when it is not purely numeric —
    /// the form used to address a single skill within a skill group by name (e.g. the
    /// <c>csharp</c> in <c>skl:languages#csharp</c>). Mutually exclusive with
    /// <see cref="Ordinal"/>.
    /// </summary>
    public string? SubKey { get; }

    /// <summary>Parses <paramref name="s"/>, throwing <see cref="FormatException"/> on invalid input.</summary>
    public static EntityId Parse(string s) => Parse(s, provider: null);

    /// <inheritdoc />
    public static EntityId Parse(string s, IFormatProvider? provider)
    {
        if (TryParseCore(s, out var result, out var error))
        {
            return result;
        }

        throw new FormatException($"'{s}' is not a valid EntityId: {error}");
    }

    /// <summary>Attempts to parse <paramref name="s"/> into an <see cref="EntityId"/>.</summary>
    public static bool TryParse(string? s, out EntityId result) => TryParseCore(s, out result, out _);

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out EntityId result) =>
        TryParseCore(s, out result, out _);

    private static bool TryParseCore(string? s, out EntityId result, out string? error)
    {
        result = default;

        if (string.IsNullOrEmpty(s))
        {
            error = "value must not be null or empty.";
            return false;
        }

        if (s == "sum")
        {
            result = new EntityId(EntityKind.Summary, string.Empty, null, null);
            error = null;
            return true;
        }

        var hashIndex = s.IndexOf('#');
        var head = hashIndex >= 0 ? s[..hashIndex] : s;
        var fragment = hashIndex >= 0 ? s[(hashIndex + 1)..] : null;

        if (fragment is { Length: 0 })
        {
            error = "the fragment after '#' must not be empty.";
            return false;
        }

        var colonIndex = head.IndexOf(':');
        if (colonIndex < 0)
        {
            error = "expected the form 'kind:slug' (or bare 'sum').";
            return false;
        }

        var kindText = head[..colonIndex];
        var slugText = head[(colonIndex + 1)..];

        if (!TryMapKind(kindText, out var kind))
        {
            error = $"unknown entity kind '{kindText}'.";
            return false;
        }

        if (kind == EntityKind.Summary)
        {
            error = "the summary id has no slug; use the bare literal 'sum'.";
            return false;
        }

        if (!IsValidSlug(slugText))
        {
            error = $"slug '{slugText}' is invalid; slugs must match ^[a-z0-9-]+$.";
            return false;
        }

        int? ordinal = null;
        string? subKey = null;

        if (fragment is not null)
        {
            if (IsNumericFragment(fragment, out var isNegative))
            {
                if (isNegative)
                {
                    error = "ordinal must not be negative.";
                    return false;
                }

                if (!int.TryParse(fragment, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    error = $"ordinal '{fragment}' is out of range.";
                    return false;
                }

                ordinal = value;
            }
            else
            {
                subKey = fragment;
            }
        }

        result = new EntityId(kind, slugText, ordinal, subKey);
        error = null;
        return true;
    }

    /// <summary>
    /// Returns a new id addressing the bullet or item at <paramref name="ordinal"/>
    /// within this entry.
    /// </summary>
    public EntityId Child(int ordinal)
    {
        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "Ordinal must not be negative.");
        }

        if (Kind == EntityKind.Summary)
        {
            throw new InvalidOperationException("The summary id cannot have children.");
        }

        return new EntityId(Kind, Slug, ordinal, null);
    }

    /// <summary>
    /// Returns a new id addressing the named sub-item <paramref name="subKey"/> within
    /// this entry (e.g. a single skill within a skill group).
    /// </summary>
    public EntityId Child(string subKey)
    {
        if (string.IsNullOrEmpty(subKey))
        {
            throw new ArgumentException("Sub-key must not be null or empty.", nameof(subKey));
        }

        if (Kind == EntityKind.Summary)
        {
            throw new InvalidOperationException("The summary id cannot have children.");
        }

        return new EntityId(Kind, Slug, null, subKey);
    }

    /// <summary>The id of the entry that owns this node, with any <c>#</c> fragment stripped.</summary>
    public EntityId Parent => Ordinal is null && SubKey is null ? this : new EntityId(Kind, Slug, null, null);

    /// <inheritdoc />
    public override string ToString()
    {
        if (Kind == EntityKind.Summary)
        {
            return "sum";
        }

        var fragment = Ordinal is { } ordinal
            ? "#" + ordinal.ToString(CultureInfo.InvariantCulture)
            : SubKey is { } subKey
                ? "#" + subKey
                : string.Empty;

        return $"{KindPrefix(Kind)}:{Slug}{fragment}";
    }

    private static string KindPrefix(EntityKind kind) => kind switch
    {
        EntityKind.Experience => "exp",
        EntityKind.Project => "prj",
        EntityKind.Education => "edu",
        EntityKind.SkillGroup => "skl",
        EntityKind.Certification => "cert",
        EntityKind.Summary => "sum",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown entity kind."),
    };

    private static bool TryMapKind(string text, out EntityKind kind)
    {
        switch (text)
        {
            case "exp":
                kind = EntityKind.Experience;
                return true;
            case "prj":
                kind = EntityKind.Project;
                return true;
            case "edu":
                kind = EntityKind.Education;
                return true;
            case "skl":
                kind = EntityKind.SkillGroup;
                return true;
            case "cert":
                kind = EntityKind.Certification;
                return true;
            case "sum":
                kind = EntityKind.Summary;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool IsValidSlug(string slug)
    {
        if (slug.Length == 0)
        {
            return false;
        }

        foreach (var c in slug)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumericFragment(string fragment, out bool isNegative)
    {
        isNegative = fragment[0] == '-';
        var start = isNegative ? 1 : 0;

        if (start == fragment.Length)
        {
            return false;
        }

        for (var i = start; i < fragment.Length; i++)
        {
            if (!char.IsAsciiDigit(fragment[i]))
            {
                return false;
            }
        }

        return true;
    }
}
