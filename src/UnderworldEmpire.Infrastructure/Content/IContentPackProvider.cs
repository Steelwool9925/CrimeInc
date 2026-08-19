namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Provides access to the active content pack for the running application.
/// Registered as a singleton — the pack is loaded once at startup.
/// </summary>
public interface IContentPackProvider
{
    /// <summary>The loaded and validated content pack.</summary>
    ContentPack ActivePack { get; }

    /// <summary>
    /// The loaded and validated job catalog for the active pack. Same lifecycle as
    /// <see cref="ActivePack"/> — read once at startup, immutable thereafter.
    /// </summary>
    JobCatalog JobCatalog { get; }

    /// <summary>
    /// The loaded and validated item catalog for the active pack. Same lifecycle as
    /// <see cref="ActivePack"/> — read once at startup, immutable thereafter.
    /// </summary>
    ItemCatalog ItemCatalog { get; }
}
