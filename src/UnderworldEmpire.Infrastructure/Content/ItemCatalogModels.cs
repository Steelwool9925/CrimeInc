using System.Text.Json.Serialization;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Mirrors the top-level <c>items.json</c> schema. Extra JSON fields are silently ignored, matching the
/// tolerance <see cref="ContentPack"/> already applies to <c>pack.json</c>.
/// </summary>
/// <remarks>
/// These are raw deserialization shapes with permissive defaults — every property is nullable or
/// defaulted so that a malformed file produces a collected validation error naming the missing field,
/// rather than a <c>JsonException</c> that names only a byte offset.
/// </remarks>
public sealed record ItemCatalogFile
{
    [JsonPropertyName("packId")]
    public string PackId { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("items")]
    public List<ItemJson> Items { get; init; } = [];
}

/// <summary>One entry in the <c>items</c> array.</summary>
public sealed record ItemJson
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Loadout slot name. Deliberately a raw string here: parsing it to
    /// <see cref="UnderworldEmpire.Domain.Items.ItemSlot"/> during deserialization would throw a
    /// <c>JsonException</c> naming a byte offset, where the validator can name the item and list the
    /// slots that do exist.
    /// </summary>
    [JsonPropertyName("slot")]
    public string Slot { get; init; } = string.Empty;

    [JsonPropertyName("levelRequirement")]
    public int LevelRequirement { get; init; }

    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("stats")]
    public ItemStatsJson Stats { get; init; } = new();
}

/// <summary>The bonus block for one item, applied while it is equipped.</summary>
public sealed record ItemStatsJson
{
    [JsonPropertyName("attack")]
    public int Attack { get; init; }

    [JsonPropertyName("defense")]
    public int Defense { get; init; }
}
