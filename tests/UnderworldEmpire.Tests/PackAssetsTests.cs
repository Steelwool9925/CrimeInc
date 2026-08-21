using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="PackAssets"/> — the pure half of asset serving (T4.1).
///
/// Resolution is driven by the manifest: a request names a logical key ("logo"), the manifest
/// says which file that key points at, and only then is a path touched. That indirection is what
/// makes the CP4 acceptance test — swap an asset, no code change — hold, so it is tested directly
/// rather than only through the endpoint.
/// </summary>
public sealed class PackAssetsTests : IDisposable
{
    private readonly string _baseDir;
    private readonly string _packRoot;

    public PackAssetsTests()
    {
        // A sibling folder outside the pack holds a real, allow-listed file. The traversal tests
        // below aim at it deliberately: if the escape target did not exist, or carried a rejected
        // extension, those tests would pass on the strength of the existence or extension check
        // and would still pass with the containment guard deleted — i.e. they would be vacuous.
        _baseDir = Path.Combine(Path.GetTempPath(), "uw-assets-" + Guid.NewGuid().ToString("N"));
        _packRoot = Path.Combine(_baseDir, "crime");

        Directory.CreateDirectory(Path.Combine(_packRoot, "assets"));
        File.WriteAllBytes(Path.Combine(_packRoot, "assets", "logo.png"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_packRoot, "assets", "icon.png"), [4, 5, 6]);

        Directory.CreateDirectory(Path.Combine(_baseDir, "outside"));
        File.WriteAllBytes(Path.Combine(_baseDir, "outside", "secret.png"), [9, 9, 9]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
            Directory.Delete(_baseDir, recursive: true);
    }

    private static ContentPack PackWith(string logo, string icon) => new()
    {
        Id = "crime",
        Assets = new AssetMap { Logo = logo, Icon = icon }
    };

    private ContentPack Standard => PackWith("assets/logo.png", "assets/icon.png");

    [Fact]
    public void Resolves_the_logo_the_manifest_declares()
    {
        var ok = PackAssets.TryResolve(Standard, _packRoot, "logo", out var path, out _);

        Assert.True(ok);
        Assert.Equal(Path.Combine(_packRoot, "assets", "logo.png"), path);
    }

    [Fact]
    public void Resolves_the_icon_the_manifest_declares()
    {
        var ok = PackAssets.TryResolve(Standard, _packRoot, "icon", out var path, out _);

        Assert.True(ok);
        Assert.Equal(Path.Combine(_packRoot, "assets", "icon.png"), path);
    }

    [Fact]
    public void Follows_the_manifest_when_a_key_is_repointed_at_another_file()
    {
        // The swap-without-code-change contract: the key is stable, the file it names is not.
        var repointed = PackWith("assets/icon.png", "assets/icon.png");

        PackAssets.TryResolve(repointed, _packRoot, "logo", out var path, out _);

        Assert.Equal(Path.Combine(_packRoot, "assets", "icon.png"), path);
    }

    [Fact]
    public void Key_lookup_ignores_case()
    {
        Assert.True(PackAssets.TryResolve(Standard, _packRoot, "LOGO", out _, out _));
    }

    [Fact]
    public void An_unknown_key_is_refused()
    {
        Assert.False(PackAssets.TryResolve(Standard, _packRoot, "banner", out _, out _));
    }

    [Fact]
    public void A_key_the_manifest_leaves_blank_is_refused()
    {
        var blank = PackWith(logo: "", icon: "assets/icon.png");

        Assert.False(PackAssets.TryResolve(blank, _packRoot, "logo", out _, out _));
    }

    [Fact]
    public void A_declared_asset_that_is_not_on_disk_is_refused()
    {
        var missing = PackWith("assets/nope.png", "assets/icon.png");

        Assert.False(PackAssets.TryResolve(missing, _packRoot, "logo", out _, out _));
    }

    [Fact]
    public void A_manifest_path_that_escapes_the_pack_root_is_refused()
    {
        // A typo'd or hostile manifest must not turn the asset route into arbitrary file read.
        // The target really exists and really is a .png, so containment is the only check that
        // can refuse it — delete that guard and this test fails.
        var escaping = PackWith("../outside/secret.png", "assets/icon.png");

        Assert.False(PackAssets.TryResolve(escaping, _packRoot, "logo", out _, out _));
    }

    [Fact]
    public void A_manifest_path_that_climbs_and_returns_is_still_refused()
    {
        // Climbing out and back in through a sibling: still outside the pack root.
        var sneaky = PackWith("assets/../../outside/secret.png", "assets/icon.png");

        Assert.False(PackAssets.TryResolve(sneaky, _packRoot, "logo", out _, out _));
    }

    [Fact]
    public void The_escape_target_used_by_the_traversal_tests_really_is_servable()
    {
        // Guards the guards: proves the traversal fixtures are not passing for an incidental
        // reason. Resolved from its own directory the same file is accepted, so the refusals
        // above are attributable to containment alone.
        var ok = PackAssets.TryResolve(
            PackWith("secret.png", "secret.png"),
            Path.Combine(_baseDir, "outside"),
            "logo",
            out _,
            out var type);

        Assert.True(ok);
        Assert.Equal("image/png", type);
    }

    [Fact]
    public void An_absolute_manifest_path_is_refused()
    {
        var absolute = PackWith(Path.Combine(Path.GetTempPath(), "elsewhere.png"), "assets/icon.png");

        Assert.False(PackAssets.TryResolve(absolute, _packRoot, "logo", out _, out _));
    }

    [Theory]
    [InlineData("assets/logo.png", "image/png")]
    [InlineData("assets/logo.jpg", "image/jpeg")]
    [InlineData("assets/logo.jpeg", "image/jpeg")]
    [InlineData("assets/logo.webp", "image/webp")]
    [InlineData("assets/logo.svg", "image/svg+xml")]
    public void Content_type_follows_the_file_extension(string relative, string expected)
    {
        var full = Path.Combine(_packRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, [1]);

        PackAssets.TryResolve(PackWith(relative, "assets/icon.png"), _packRoot, "logo", out _, out var type);

        Assert.Equal(expected, type);
    }

    [Fact]
    public void An_unrecognised_extension_is_refused()
    {
        var full = Path.Combine(_packRoot, "assets", "logo.exe");
        File.WriteAllBytes(full, [1]);

        Assert.False(PackAssets.TryResolve(PackWith("assets/logo.exe", "assets/icon.png"), _packRoot, "logo", out _, out _));
    }

    [Fact]
    public void The_declared_keys_are_logo_and_icon()
    {
        Assert.Equal(["icon", "logo"], PackAssets.Keys.Order());
    }
}
