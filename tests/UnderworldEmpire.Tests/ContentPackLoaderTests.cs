using System.Text.Json;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="ContentPackLoader"/> (T1.4).
/// All tests spin up a temp directory — no web host required.
/// </summary>
public class ContentPackLoaderTests : IDisposable
{
    // Each test gets its own isolated temp root so they never interfere.
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ue-content-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Writes a pack.json into _tempRoot/<paramref name="packId"/>/pack.json.</summary>
    private void WritePack(string packId, object payload)
    {
        var dir = Path.Combine(_tempRoot, packId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "pack.json"),
            JsonSerializer.Serialize(payload));
    }

    private ContentPackLoader Loader() => new(_tempRoot);

    // -------------------------------------------------------------------------
    // Happy-path
    // -------------------------------------------------------------------------

    [Fact]
    public void Valid_pack_loads_and_exposes_name_and_currency_labels()
    {
        WritePack("crime", new
        {
            id = "crime",
            name = "Underworld Empire",
            currencies = new
            {
                cash    = new { label = "Cash",      abbr = "$" },
                premium = new { label = "Gold Bars", abbr = "GB" }
            },
            palette = new { primary = "#B71C1C", secondary = "#212121", accent = "#FFC107" },
            assets  = new { logo = "assets/logo.png", icon = "assets/icon.png" },
            version = 1
        });

        var pack = Loader().Load("crime");

        Assert.Equal("Underworld Empire", pack.Name);
        Assert.Equal("Cash",      pack.Currencies.Cash.Label);
        Assert.Equal("Gold Bars", pack.Currencies.Premium.Label);
    }

    [Fact]
    public void Valid_pack_tolerates_extra_unknown_fields()
    {
        // The loader must not throw when pack.json contains fields the model doesn't know.
        WritePack("crime", new
        {
            id = "crime",
            name = "Underworld Empire",
            currencies = new
            {
                cash    = new { label = "Cash",      abbr = "$" },
                premium = new { label = "Gold Bars", abbr = "GB" }
            },
            palette    = new { primary = "#000", secondary = "#111", accent = "#222" },
            assets     = new { logo = "a.png", icon = "b.png" },
            version    = 1,
            futureField = "I am from the future"
        });

        var pack = Loader().Load("crime");

        Assert.Equal("Underworld Empire", pack.Name);
    }

    // -------------------------------------------------------------------------
    // Validation failures
    // -------------------------------------------------------------------------

    [Fact]
    public void Missing_name_throws_ContentPackException()
    {
        WritePack("crime", new
        {
            id   = "crime",
            // name intentionally omitted
            currencies = new
            {
                cash    = new { label = "Cash",      abbr = "$" },
                premium = new { label = "Gold Bars", abbr = "GB" }
            }
        });

        var ex = Assert.Throws<ContentPackException>(() => Loader().Load("crime"));
        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_premium_currency_label_throws_ContentPackException()
    {
        WritePack("crime", new
        {
            id   = "crime",
            name = "Underworld Empire",
            currencies = new
            {
                cash    = new { label = "Cash", abbr = "$" },
                premium = new { label = "",     abbr = "GB" }  // empty label
            }
        });

        var ex = Assert.Throws<ContentPackException>(() => Loader().Load("crime"));
        Assert.Contains("premium", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Id_mismatch_between_folder_and_json_throws_ContentPackException()
    {
        // Folder says "crime" but the json says "fantasy".
        WritePack("crime", new
        {
            id   = "fantasy",   // wrong
            name = "Fantasy Realm",
            currencies = new
            {
                cash    = new { label = "Gold",   abbr = "G" },
                premium = new { label = "Gems",   abbr = "Gem" }
            }
        });

        var ex = Assert.Throws<ContentPackException>(() => Loader().Load("crime"));
        Assert.Contains("mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_pack_id_with_missing_file_throws_ContentPackException()
    {
        // No pack.json written at all — directory doesn't even exist.
        var ex = Assert.Throws<ContentPackException>(() => Loader().Load("nosuchpack"));
        Assert.Contains("nosuchpack", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Real crime pack (from repo root content/crime)
    // -------------------------------------------------------------------------

    [Fact]
    public void Real_crime_pack_loads_with_correct_currency_labels()
    {
        // Resolve the repo content folder relative to this assembly's location.
        // Tests run from: tests/UnderworldEmpire.Tests/bin/<config>/net10.0/
        // The Api csproj copies content/ to its own output, but the test project does not.
        // Walk up until we find the repo root (contains the 'content' folder).
        var dir = AppContext.BaseDirectory;
        string? contentRoot = null;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "content");
            if (Directory.Exists(candidate))
            {
                contentRoot = candidate;
                break;
            }
            var parent = Path.GetDirectoryName(dir);
            if (parent is null) break;
            dir = parent;
        }

        Assert.NotNull(contentRoot);  // guard: repo root must be reachable

        var loader = new ContentPackLoader(contentRoot);
        var pack   = loader.Load("crime");

        Assert.Equal("Cash",      pack.Currencies.Cash.Label);
        Assert.Equal("Gold Bars", pack.Currencies.Premium.Label);
    }
}
