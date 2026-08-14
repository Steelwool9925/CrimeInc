namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Provides access to the active content pack for the running application.
/// Registered as a singleton — the pack is loaded once at startup.
/// </summary>
public interface IContentPackProvider
{
    /// <summary>The loaded and validated content pack.</summary>
    ContentPack ActivePack { get; }
}
