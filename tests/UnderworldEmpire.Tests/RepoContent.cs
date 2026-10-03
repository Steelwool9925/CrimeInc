namespace UnderworldEmpire.Tests;

/// <summary>
/// Locates the repository's real <c>content/</c> folder from inside the test assembly.
/// </summary>
/// <remarks>
/// <para>
/// The test project deliberately does not copy the content packs to its output directory — the
/// invariant tests are only meaningful against the packs that actually ship, not a stale copy
/// taken at build time. So the tests have to walk up from the assembly location to the repo root
/// to find them.
/// </para>
/// <para>
/// This lived as four byte-identical private copies (in the job, item and property catalog-loader
/// tests, and then <c>FirstSessionTests</c>). One of the loop bound, the folder name and the
/// failure message is enough; four means a fix to the walk has to be found four times.
/// </para>
/// </remarks>
internal static class RepoContent
{
    /// <summary>The absolute path of the repo's <c>content</c> folder.</summary>
    /// <exception cref="DirectoryNotFoundException">
    /// If no <c>content</c> folder is found within ten levels above the test assembly — which
    /// means the output layout moved, and every content-invariant test is about to be meaningless.
    /// Failing loudly here is the point.
    /// </exception>
    internal static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "content");
            if (Directory.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(dir);
            if (parent is null) break;
            dir = parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repo 'content' folder from the test assembly.");
    }
}
