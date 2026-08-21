using System.Diagnostics.CodeAnalysis;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Resolves a logical asset key ("logo", "icon") to a file inside the active pack (T4.1).
///
/// Requests never name a path — they name a key, and the manifest decides which file that key
/// points at. Swapping the art is therefore a content edit, which is the whole point of the
/// manifest-driven theming contract that CP4 tests.
///
/// Pure and side-effect-free apart from the existence probe, so the traversal guards below are
/// unit-testable without booting a host.
/// </summary>
public static class PackAssets
{
    /// <summary>Content types this resolver is willing to serve, keyed by file extension.</summary>
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml"
    };

    /// <summary>The logical asset keys the manifest declares.</summary>
    public static IReadOnlyCollection<string> Keys { get; } = ["logo", "icon"];

    /// <summary>
    /// Resolves <paramref name="key"/> against the manifest and the pack folder.
    /// Returns false — never throws — for an unknown key, a blank or missing declaration, a path
    /// that escapes <paramref name="packRoot"/>, or a file type not on the allow-list.
    /// </summary>
    public static bool TryResolve(
        ContentPack pack,
        string packRoot,
        string key,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(true)] out string? contentType)
    {
        fullPath = null;
        contentType = null;

        var relative = key?.ToLowerInvariant() switch
        {
            "logo" => pack.Assets.Logo,
            "icon" => pack.Assets.Icon,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(relative))
            return false;

        // An absolute declaration would sidestep the containment check below, so refuse it
        // outright rather than trying to reason about where it points.
        if (Path.IsPathRooted(relative))
            return false;

        var root = Path.GetFullPath(packRoot);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        // GetFullPath collapses any "..", so a climb out of the pack folder shows up here as a
        // resolved path that no longer sits under the root.
        var candidate = Path.GetFullPath(Path.Combine(root, relative));
        if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!ContentTypes.TryGetValue(Path.GetExtension(candidate), out var resolvedType))
            return false;

        if (!File.Exists(candidate))
            return false;

        fullPath = candidate;
        contentType = resolvedType;
        return true;
    }
}
