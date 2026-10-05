using System.Text.Json.Serialization;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Mirrors the top-level <c>properties.json</c> schema. Extra JSON fields are silently ignored, matching
/// the tolerance <see cref="ItemCatalogFile"/> already applies to <c>items.json</c>.
/// </summary>
/// <remarks>
/// These are raw deserialization shapes with permissive defaults — every property is nullable or
/// defaulted so that a malformed file produces a collected validation error naming the missing field,
/// rather than a <c>JsonException</c> that names only a byte offset.
/// </remarks>
public sealed record PropertyCatalogFile
{
    [JsonPropertyName("packId")]
    public string PackId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("properties")]
    public List<PropertyJson> Properties { get; init; } = [];
}

/// <summary>One entry in the <c>properties</c> array.</summary>
public sealed record PropertyJson
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("levelRequirement")]
    public int LevelRequirement { get; init; }

    [JsonPropertyName("price")]
    public long Price { get; init; }

    /// <summary>
    /// Most units one player may hold. Absent means no limit; the shipped pack always declares one
    /// (see <c>Property.MaxUnits</c> for why).
    /// </summary>
    [JsonPropertyName("maxUnits")]
    public int? MaxUnits { get; init; }

    /// <summary>
    /// The income block. Defaulted rather than nullable so an entry that omits it reports "perHour must
    /// be >= 1" alongside every other problem in the file, instead of a null dereference on the first
    /// entry that happens to be missing it.
    /// </summary>
    [JsonPropertyName("income")]
    public PropertyIncomeJson Income { get; init; } = new();
}

/// <summary>The income block for one property: what a unit earns, and how long it holds it.</summary>
public sealed record PropertyIncomeJson
{
    [JsonPropertyName("perHour")]
    public long PerHour { get; init; }

    [JsonPropertyName("capMinutes")]
    public int CapMinutes { get; init; }
}
