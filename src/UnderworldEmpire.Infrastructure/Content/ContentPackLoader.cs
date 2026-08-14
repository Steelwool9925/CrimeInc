using System.Text;
using System.Text.Json;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Loads a content pack by id from disk and validates required fields.
/// Resolves packs from <c>&lt;contentRoot&gt;/&lt;packId&gt;/pack.json</c>.
/// The content root defaults to <c>AppContext.BaseDirectory/content</c> and can be
/// overridden via the <c>Content:Root</c> configuration key.
/// </summary>
public sealed class ContentPackLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // Tolerate extra fields the engine does not yet know about.
        UnknownTypeHandling = System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonElement,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _contentRoot;

    /// <param name="contentRoot">
    ///     Base directory that contains per-pack sub-folders. Defaults to
    ///     <c>AppContext.BaseDirectory/content</c> when <see langword="null"/> or empty.
    /// </param>
    public ContentPackLoader(string? contentRoot = null)
    {
        _contentRoot = string.IsNullOrWhiteSpace(contentRoot)
            ? Path.Combine(AppContext.BaseDirectory, "content")
            : contentRoot;
    }

    /// <summary>
    /// Loads and validates the pack with <paramref name="packId"/>.
    /// </summary>
    /// <exception cref="ContentPackException">
    ///     Thrown when the pack file is missing, cannot be parsed, or fails validation.
    /// </exception>
    public ContentPack Load(string packId)
    {
        var packFile = Path.Combine(_contentRoot, packId, "pack.json");

        if (!File.Exists(packFile))
            throw new ContentPackException(
                $"Content pack '{packId}' not found at '{packFile}'.");

        ContentPack pack;
        try
        {
            var json = File.ReadAllText(packFile, Encoding.UTF8);
            pack = JsonSerializer.Deserialize<ContentPack>(json, JsonOptions)
                   ?? throw new ContentPackException(
                       $"pack.json for '{packId}' deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new ContentPackException(
                $"pack.json for '{packId}' is not valid JSON: {ex.Message}", ex);
        }

        Validate(pack, packId);
        return pack;
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    private static void Validate(ContentPack pack, string expectedId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(pack.Id))
            errors.Add("'id' is missing or empty");
        else if (!string.Equals(pack.Id, expectedId, StringComparison.Ordinal))
            errors.Add($"'id' mismatch: expected '{expectedId}', found '{pack.Id}'");

        if (string.IsNullOrWhiteSpace(pack.Name))
            errors.Add("'name' is missing or empty");

        if (string.IsNullOrWhiteSpace(pack.Currencies.Cash.Label))
            errors.Add("'currencies.cash.label' is missing or empty");

        if (string.IsNullOrWhiteSpace(pack.Currencies.Premium.Label))
            errors.Add("'currencies.premium.label' is missing or empty");

        if (errors.Count > 0)
            throw new ContentPackException(
                $"Content pack '{expectedId}' failed validation: {string.Join("; ", errors)}.");
    }
}
