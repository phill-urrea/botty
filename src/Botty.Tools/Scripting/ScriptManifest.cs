using System.Text.Json.Serialization;

namespace Botty.Tools.Scripting;

public class ScriptManifest
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("interpreter")]
    public required string Interpreter { get; set; }

    [JsonPropertyName("entrypoint")]
    public required string Entrypoint { get; set; }

    [JsonPropertyName("parameters")]
    public ScriptParameters? Parameters { get; set; }

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

public class ScriptParameters
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, ScriptParameterProperty> Properties { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }

    [JsonPropertyName("additionalProperties")]
    public bool? AdditionalProperties { get; set; }
}

public class ScriptParameterProperty
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("enum")]
    public List<string>? Enum { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("items")]
    public ScriptParameterProperty? Items { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, ScriptParameterProperty>? Properties { get; set; }

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }

    [JsonPropertyName("additionalProperties")]
    public bool? AdditionalProperties { get; set; }
}
