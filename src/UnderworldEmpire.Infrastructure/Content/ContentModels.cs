using System.Text.Json.Serialization;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>Mirrors the top-level pack.json schema. Extra JSON fields are silently ignored.</summary>
public sealed record ContentPack
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("currencies")]
    public CurrencyLabels Currencies { get; init; } = new();

    [JsonPropertyName("palette")]
    public Palette Palette { get; init; } = new();

    [JsonPropertyName("assets")]
    public AssetMap Assets { get; init; } = new();

    [JsonPropertyName("version")]
    public int Version { get; init; }
}

/// <summary>Currency label section of the pack.</summary>
public sealed record CurrencyLabels
{
    [JsonPropertyName("cash")]
    public CurrencyLabel Cash { get; init; } = new();

    [JsonPropertyName("premium")]
    public CurrencyLabel Premium { get; init; } = new();
}

/// <summary>Display labels for one currency.</summary>
public sealed record CurrencyLabel
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("abbr")]
    public string Abbr { get; init; } = string.Empty;
}

/// <summary>Theme colour palette.</summary>
public sealed record Palette
{
    [JsonPropertyName("primary")]
    public string Primary { get; init; } = string.Empty;

    [JsonPropertyName("secondary")]
    public string Secondary { get; init; } = string.Empty;

    [JsonPropertyName("accent")]
    public string Accent { get; init; } = string.Empty;
}

/// <summary>Paths to key art assets (relative to the pack folder).</summary>
public sealed record AssetMap
{
    [JsonPropertyName("logo")]
    public string Logo { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;
}
