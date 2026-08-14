namespace UnderworldEmpire.Domain;

/// <summary>
/// Minimal domain constant used to prove cross-project wiring at scaffold time.
/// The MVP ships only the <c>crime</c> content pack (see plan §Content).
/// </summary>
public static class BuildInfo
{
    public const string DefaultContentPack = "crime";
}
