using System.Text;

namespace ResumeForge.Domain.Text;

/// <summary>
/// Normalizes skill names to the stable match form used for comparison and deduplication
/// everywhere in the system (e.g. skill tags, job description matching).
/// </summary>
public static class SkillNormalizer
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C++"] = "cpp",
        ["C#"] = "csharp",
        [".NET"] = "dotnet",
        ["Node.js"] = "nodejs",
        ["JS"] = "javascript",
        ["TS"] = "typescript",
        ["K8s"] = "kubernetes",
        ["Postgres"] = "postgresql",
        ["PostgreSQL"] = "postgresql",
        ["GH Actions"] = "githubactions",
        ["GitHub Actions"] = "githubactions",
    };

    /// <summary>
    /// Produces the normalized match form of <paramref name="skill"/>: lowercase, with
    /// whitespace and punctuation stripped, except '+' and '#' which map to the distinct
    /// tokens "plus" and "sharp" respectively. A small alias table covers common
    /// abbreviations and ambiguous cases (e.g. "C#" → "csharp", "K8s" → "kubernetes")
    /// before the general rule is applied. Deterministic for a given input.
    /// </summary>
    public static string Normalize(string skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var trimmed = skill.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return Aliases.TryGetValue(trimmed, out var alias) ? alias : ApplyGeneralRule(trimmed);
    }

    private static string ApplyGeneralRule(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch == '+')
            {
                builder.Append("plus");
            }
            else if (ch == '#')
            {
                builder.Append("sharp");
            }
            // all other whitespace and punctuation is stripped
        }

        return builder.ToString();
    }
}
