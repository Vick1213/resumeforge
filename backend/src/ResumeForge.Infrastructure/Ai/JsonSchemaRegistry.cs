using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResumeForge.Application.Tailoring;
using ResumeForge.Domain.Resume;

namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// Holds the JSON Schema documents named by <c>ModelRequest.SchemaName</c>. Used by
/// <see cref="AnthropicLanguageModel"/> to force structured tool-use output. At minimum
/// covers <c>"tailor-commands"</c> (CONTRACTS.md §6) and <c>"field-resolutions"</c>
/// (CONTRACTS.md §10).
/// </summary>
public sealed class JsonSchemaRegistry
{
    /// <summary>The schema for the tailoring command list the model proposes.</summary>
    public const string TailorCommandsSchemaName = "tailor-commands";

    /// <summary>The schema for the autofill field-resolution list the model proposes.</summary>
    public const string FieldResolutionsSchemaName = "field-resolutions";

    /// <summary>
    /// The schema for the ATS review the first tailoring model call produces (CONTRACTS.md
    /// §6 "ATS review pass"): what the posting asks for that the base resume does not
    /// evidence, and the screen score before and after closing those gaps.
    /// </summary>
    public const string AtsReviewSchemaName = "ats-review";

    private readonly FrozenDictionary<string, JsonDocument> _schemas;
    private readonly FrozenDictionary<string, string> _payloadPropertyNames;

    /// <summary>Parses every bundled schema document once, and derives each one's payload property.</summary>
    public JsonSchemaRegistry()
    {
        _schemas = new Dictionary<string, JsonDocument>(StringComparer.Ordinal)
        {
            [TailorCommandsSchemaName] = JsonDocument.Parse(TailorCommandsSchema),
            [FieldResolutionsSchemaName] = JsonDocument.Parse(FieldResolutionsSchema),
            [AtsReviewSchemaName] = JsonDocument.Parse(AtsReviewSchema),
        }.ToFrozenDictionary();

        _payloadPropertyNames = _schemas.ToDictionary(
            kvp => kvp.Key,
            kvp => DerivePayloadPropertyName(kvp.Key, kvp.Value.RootElement),
            StringComparer.Ordinal).ToFrozenDictionary();
    }

    /// <summary>The names of every registered schema.</summary>
    public IReadOnlyCollection<string> SchemaNames => _schemas.Keys;

    /// <summary>Attempts to retrieve the JSON Schema document registered under <paramref name="schemaName"/>.</summary>
    public bool TryGetSchema(string schemaName, out JsonElement schema)
    {
        if (_schemas.TryGetValue(schemaName, out var document))
        {
            schema = document.RootElement;
            return true;
        }

        schema = default;
        return false;
    }

    /// <summary>Retrieves the JSON Schema document registered under <paramref name="schemaName"/>.</summary>
    public JsonElement GetSchema(string schemaName) =>
        TryGetSchema(schemaName, out var schema)
            ? schema
            : throw new KeyNotFoundException($"No JSON schema is registered for '{schemaName}'.");

    /// <summary>
    /// The name of the single top-level property under which <paramref name="schemaName"/>
    /// carries its actual payload (e.g. <c>"commands"</c> for <see cref="TailorCommandsSchemaName"/>).
    /// OpenAI-style function calling requires a tool's <c>parameters</c> — and therefore this
    /// registry's every schema — to be <c>type: "object"</c> at the top level, even though
    /// the caller ultimately wants a bare array or other value out of it. Every language-model
    /// client must unwrap this property before deserializing a response into the caller's
    /// requested type, so this lives on the registry, next to the schema itself, rather than
    /// being duplicated (and risking drift) in each client.
    /// </summary>
    public string GetPayloadPropertyName(string schemaName) =>
        _payloadPropertyNames.TryGetValue(schemaName, out var name)
            ? name
            : throw new KeyNotFoundException($"No JSON schema is registered for '{schemaName}'.");

    /// <summary>
    /// Derives <paramref name="schemaName"/>'s payload property directly from its own
    /// <c>properties</c> object rather than declaring it separately, so the schema and its
    /// wrapper name can never drift apart: a single-property object schema has exactly one
    /// possible payload property, by construction. A schema that ever needs more than one
    /// top-level property fails fast here (at registry construction, i.e. process startup)
    /// rather than silently guessing — at that point the payload property should be declared
    /// explicitly instead.
    /// </summary>
    private static string DerivePayloadPropertyName(string schemaName, JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Schema '{schemaName}' has no 'properties' object to derive a payload property from.");
        }

        var names = new List<string>();
        foreach (var property in properties.EnumerateObject())
        {
            names.Add(property.Name);
        }

        return names.Count == 1
            ? names[0]
            : throw new InvalidOperationException(
                $"Schema '{schemaName}' declares {names.Count} top-level properties; a payload property can only be " +
                "derived automatically for a single-property object schema. Declare it explicitly instead.");
    }

    /// <summary>
    /// Derives the exact set of legal wire values for <typeparamref name="TEnum"/> — the same
    /// values <see cref="JsonStringEnumConverter"/> would actually produce, respecting any
    /// per-member <see cref="JsonStringEnumMemberNameAttribute"/> override (see
    /// <c>ResumeForge.Domain.Knowledge.KnowledgeSource.GitHub</c> for an example) and falling
    /// back to <see cref="JsonNamingPolicy.CamelCase"/> otherwise, exactly like the global
    /// converter every language-model client's <c>SerializerOptions</c> and the API host both
    /// register. Schema `enum` constraints are built from this rather than hand-typed so a
    /// new enum member can never silently go unconstrained: forgetting to add a member here
    /// is impossible, because there's nothing to add — the list is recomputed from the enum
    /// type itself every time the registry is constructed.
    /// </summary>
    private static string[] WireValuesOf<TEnum>() where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var wireValues = new string[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            var member = typeof(TEnum).GetField(values[i].ToString())
                ?? throw new InvalidOperationException($"Enum member '{values[i]}' of {typeof(TEnum)} has no backing field.");
            var overrideName = member.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name;
            wireValues[i] = overrideName ?? JsonNamingPolicy.CamelCase.ConvertName(values[i].ToString());
        }

        return wireValues;
    }

    /// <summary>The legal <see cref="SectionKind"/> wire values, as a JSON array literal.</summary>
    private static string SectionKindEnumJson => JsonSerializer.Serialize(WireValuesOf<SectionKind>());

    /// <summary>The legal <see cref="AtsGapImportance"/> wire values, as a JSON array literal.</summary>
    private static string AtsGapImportanceEnumJson => JsonSerializer.Serialize(WireValuesOf<AtsGapImportance>());

    private static readonly string AtsReviewSchema = $$"""
        {
          "type": "object",
          "description": "A screener's reading of one resume against one job posting.",
          "properties": {
            "review": {
              "type": "object",
              "properties": {
                "scoreBefore": {
                  "type": "integer", "minimum": 0, "maximum": 100,
                  "description": "How the resume scores against this posting as it stands, before any change."
                },
                "scoreAfter": {
                  "type": "integer", "minimum": 0, "maximum": 100,
                  "description": "What the same resume would score once every gap listed below is closed."
                },
                "verdict": {
                  "type": "string", "maxLength": 600,
                  "description": "One or two sentences on why the resume scores where it does."
                },
                "gaps": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "keyword": {
                        "type": "string", "maxLength": 80,
                        "description": "The posting's own term, spelled the way the posting spells it."
                      },
                      "importance": { "type": "string", "enum": {{AtsGapImportanceEnumJson}} },
                      "skillsOnly": {
                        "type": "boolean",
                        "description": "True when the resume lists the term among its skills but no bullet puts it in context."
                      },
                      "placement": {
                        "type": ["string", "null"],
                        "description": "An entry or bullet id from the resume given, or a section name when no single bullet fits."
                      },
                      "angle": {
                        "type": "string", "maxLength": 400,
                        "description": "The claim this keyword should evidence, drawn only from material the resume already contains: what the candidate did with it and what it moved."
                      }
                    },
                    "required": ["keyword", "importance", "skillsOnly", "angle"],
                    "additionalProperties": false
                  }
                },
                "recruiterNotes": {
                  "type": "array",
                  "items": { "type": "string", "maxLength": 400 },
                  "description": "What a human reviewer would hold against the resume beyond keyword matching."
                }
              },
              "required": ["scoreBefore", "scoreAfter", "verdict", "gaps", "recruiterNotes"],
              "additionalProperties": false
            }
          },
          "required": ["review"],
          "additionalProperties": false
        }
        """;

    private static readonly string TailorCommandsSchema = $$"""
        {
          "type": "object",
          "description": "The list of tailoring commands proposed for this run.",
          "properties": {
            "commands": {
              "type": "array",
              "items": {
                "oneOf": [
                  {
                    "type": "object",
                    "properties": {
                      "op": { "const": "include" },
                      "targets": { "type": "array", "items": { "type": "string" } },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "targets"],
                    "additionalProperties": false
                  },
                  {
                    "type": "object",
                    "properties": {
                      "op": { "const": "exclude" },
                      "targets": { "type": "array", "items": { "type": "string" } },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "targets"],
                    "additionalProperties": false
                  },
                  {
                    "type": "object",
                    "properties": {
                      "op": { "const": "order" },
                      "parent": { "type": "string" },
                      "order": { "type": "array", "items": { "type": "string" } },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "parent", "order"],
                    "additionalProperties": false
                  },
                  {
                    "type": "object",
                    "properties": {
                      "op": { "const": "selectVariant" },
                      "target": { "type": "string" },
                      "variantIndex": { "type": "integer", "minimum": 0 },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "target", "variantIndex"],
                    "additionalProperties": false
                  },
                  {
                    "type": "object",
                    "properties": {
                      "op": { "const": "rewrite" },
                      "target": { "type": "string" },
                      "text": { "type": "string", "maxLength": 300 },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "target", "text"],
                    "additionalProperties": false
                  },
                  {
                    "type": "object",
                    "properties": {
                      "op": { "const": "setSummary" },
                      "text": { "type": "string" },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "text"],
                    "additionalProperties": false
                  },
                  {
                    "type": "object",
                    "properties": {
                      "op": { "const": "setTagline" },
                      "target": { "type": "string" },
                      "text": { "type": "string", "maxLength": 300 },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "target", "text"],
                    "additionalProperties": false
                  },
                  {
                    "type": "object",
                    "properties": {
                      "op": { "const": "emphasizeSkills" },
                      "skills": { "type": "array", "items": { "type": "string" } },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "skills"],
                    "additionalProperties": false
                  },
                  {
                    "type": "object",
                    "properties": {
                      "op": { "const": "setSectionOrder" },
                      "order": {
                        "type": "array",
                        "items": {
                          "type": "string",
                          "enum": {{SectionKindEnumJson}}
                        }
                      },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "order"],
                    "additionalProperties": false
                  },
                  {
                    "type": "object",
                    "description": "Only available at Thorough effort and above, and only for keywords the knowledge base already evidences.",
                    "properties": {
                      "op": { "const": "injectKeywords" },
                      "target": { "type": "string" },
                      "keywords": { "type": "array", "items": { "type": "string" } },
                      "text": { "type": "string", "maxLength": 300 },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "target", "keywords", "text"],
                    "additionalProperties": false
                  }
                ]
              }
            }
          },
          "required": ["commands"],
          "additionalProperties": false
        }
        """;

    private const string FieldResolutionsSchema = """
        {
          "type": "object",
          "description": "The list of autofill field resolutions proposed for the unresolved fields given.",
          "properties": {
            "resolutions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "elementId": { "type": "string" },
                  "canonicalKey": {
                    "type": "string",
                    "description": "One of the canonical autofill field keys, or an empty string when genuinely unmappable."
                  },
                  "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                  "optionValue": { "type": ["string", "null"] }
                },
                "required": ["elementId", "canonicalKey", "confidence"],
                "additionalProperties": false
              }
            }
          },
          "required": ["resolutions"],
          "additionalProperties": false
        }
        """;
}
