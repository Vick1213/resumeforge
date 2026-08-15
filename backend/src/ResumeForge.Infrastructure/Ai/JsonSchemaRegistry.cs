using System.Collections.Frozen;
using System.Text.Json;

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

    private readonly FrozenDictionary<string, JsonDocument> _schemas;

    /// <summary>Parses every bundled schema document once.</summary>
    public JsonSchemaRegistry()
    {
        _schemas = new Dictionary<string, JsonDocument>(StringComparer.Ordinal)
        {
            [TailorCommandsSchemaName] = JsonDocument.Parse(TailorCommandsSchema),
            [FieldResolutionsSchemaName] = JsonDocument.Parse(FieldResolutionsSchema),
        }.ToFrozenDictionary();
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

    private const string TailorCommandsSchema = """
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
                          "enum": ["summary", "skills", "experience", "projects", "education", "certifications"]
                        }
                      },
                      "rationale": { "type": ["string", "null"] }
                    },
                    "required": ["op", "order"],
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
