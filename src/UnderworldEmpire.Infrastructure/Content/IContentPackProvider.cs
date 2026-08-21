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
    /// Absolute path of the active pack's folder. Asset requests resolve the manifest's relative
    /// asset paths against this and are confined to it — see <see cref="PackAssets.TryResolve"/>.
    /// </summary>
    string PackRoot { get; }

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

    /// <summary>
    /// The loaded and validated property catalog for the active pack. Same lifecycle as
    /// <see cref="ActivePack"/> — read once at startup, immutable thereafter.
    /// </summary>
    PropertyCatalog PropertyCatalog { get; }
}
