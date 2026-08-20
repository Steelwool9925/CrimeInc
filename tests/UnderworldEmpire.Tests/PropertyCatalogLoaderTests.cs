using System.Text.Json;

using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="ContentPackLoader.LoadProperties"/> and <see cref="PropertyCatalog"/>
/// (T3.3). Mirrors <see cref="ItemCatalogLoaderTests"/>: each test writes a temp properties.json — no
/// web host and no database required.
/// </summary>
public class PropertyCatalogLoaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ue-props-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void WriteProperties(string packId, object payload)
    {
        var dir = Path.Combine(_tempRoot, packId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "properties.json"), JsonSerializer.Serialize(payload));
    }

    private void WriteRawProperties(string packId, string rawJson)
    {
        var dir = Path.Combine(_tempRoot, packId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "properties.json"), rawJson);
    }

    private ContentPackLoader Loader() => new(_tempRoot);

    /// <summary>A minimal valid catalog; individual tests override the parts they care about.</summary>
    private static object ValidCatalog(params object[] properties) => new
    {
        packId = "crime",
        version = 1,
        properties = properties.Length > 0 ? properties : [ValidProperty()]
    };

    private static object ValidProperty(
        string id = "street-corner",
        string name = "Street Corner",
        int levelRequirement = 1,
        long price = 250,
        long perHour = 120,
        int capMinutes = 30) => new
        {
            id,
            name,
            description = "d",
            levelRequirement,
            price,
            income = new { perHour, capMinutes }
        };

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public void Valid_catalog_loads_properties()
    {
        WriteProperties("crime", ValidCatalog());

        var catalog = Loader().LoadProperties("crime");

        var property = Assert.Single(catalog.Properties);
        Assert.Equal("street-corner", property.Id);
        Assert.Equal("Street Corner", property.Name);
        Assert.Equal("d", property.Description);
        Assert.Equal(1, property.LevelRequirement);
        Assert.Equal(250, property.Price);
        Assert.Equal(120, property.IncomePerHour);
        Assert.Equal(30, property.AccrualCapMinutes);
    }

    [Fact]
    public void Properties_keep_the_order_content_declared_them_in()
    {
        WriteProperties("crime", ValidCatalog(
            ValidProperty(id: "casino", name: "Casino"),
            ValidProperty(id: "street-corner", name: "Street Corner")));

        var catalog = Loader().LoadProperties("crime");

        Assert.Equal(["casino", "street-corner"], catalog.Properties.Select(p => p.Id));
    }

    [Fact]
    public void Unknown_fields_are_ignored_so_a_newer_pack_still_loads()
    {
        WriteRawProperties("crime", """
        {
          "packId": "crime",
          "version": 1,
          "unheardOf": true,
          "properties": [
            {
              "id": "street-corner", "name": "Street Corner", "description": "d",
              "levelRequirement": 1, "price": 250,
              "income": { "perHour": 120, "capMinutes": 30 },
              "artwork": "future.png"
            }
          ]
        }
        """);

        Assert.Single(Loader().LoadProperties("crime").Properties);
    }

    [Fact]
    public void A_property_is_found_by_its_content_id()
    {
        WriteProperties("crime", ValidCatalog());

        var catalog = Loader().LoadProperties("crime");

        Assert.Equal("Street Corner", catalog.Find("street-corner")?.Name);
    }

    [Fact]
    public void Property_ids_are_compared_ordinally()
    {
        WriteProperties("crime", ValidCatalog());

        var catalog = Loader().LoadProperties("crime");

        Assert.Null(catalog.Find("Street-Corner"));
    }

    [Fact]
    public void An_unknown_id_finds_nothing_rather_than_throwing()
    {
        WriteProperties("crime", ValidCatalog());

        Assert.Null(Loader().LoadProperties("crime").Find("no-such-property"));
    }

    // -------------------------------------------------------------------------
    // File-level failures
    // -------------------------------------------------------------------------

    [Fact]
    public void A_missing_file_is_reported_as_a_content_error()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "crime"));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("properties.json", ex.Message);
    }

    [Fact]
    public void Malformed_json_is_reported_as_a_content_error()
    {
        WriteRawProperties("crime", "{ not json");

        Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    [Fact]
    public void A_pack_id_that_does_not_match_the_folder_is_rejected()
    {
        WriteProperties("crime", new { packId = "fantasy", version = 1, properties = new[] { ValidProperty() } });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("packId", ex.Message);
    }

    [Fact]
    public void An_empty_catalog_is_rejected()
    {
        WriteProperties("crime", new { packId = "crime", version = 1, properties = Array.Empty<object>() });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("properties", ex.Message);
    }

    [Fact]
    public void A_blank_id_is_rejected_and_named_by_its_array_index()
    {
        WriteProperties("crime", ValidCatalog(ValidProperty(id: "")));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("properties[0]", ex.Message);
    }

    [Fact]
    public void A_duplicate_id_is_rejected()
    {
        WriteProperties("crime", ValidCatalog(ValidProperty(), ValidProperty()));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("duplicate", ex.Message);
    }

    [Fact]
    public void An_id_too_long_for_its_database_column_is_rejected_at_startup()
    {
        WriteProperties("crime", ValidCatalog(ValidProperty(id: new string('x', 65))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("64", ex.Message);
    }

    [Fact]
    public void A_blank_name_is_rejected()
    {
        WriteProperties("crime", ValidCatalog(ValidProperty(name: " ")));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void A_level_requirement_below_one_is_rejected()
    {
        WriteProperties("crime", ValidCatalog(ValidProperty(levelRequirement: 0)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("levelRequirement", ex.Message);
    }

    [Fact]
    public void A_negative_price_is_rejected()
    {
        WriteProperties("crime", ValidCatalog(ValidProperty(price: -1)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("price", ex.Message);
    }

    [Fact]
    public void A_property_that_earns_nothing_per_hour_is_rejected()
    {
        WriteProperties("crime", ValidCatalog(ValidProperty(perHour: 0)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("income.perHour", ex.Message);
    }

    [Fact]
    public void A_non_positive_accrual_window_is_rejected()
    {
        WriteProperties("crime", ValidCatalog(ValidProperty(capMinutes: 0)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("income.capMinutes", ex.Message);
    }

    [Fact]
    public void A_rate_and_window_that_multiply_out_to_no_cash_are_rejected()
    {
        // 30/hour for one minute is half a coin, which floors to nothing. Both fields are individually
        // legal, so only the product catches it — and catching it here beats a live property that
        // charges 250 and then never pays.
        WriteProperties("crime", ValidCatalog(ValidProperty(perHour: 30, capMinutes: 1)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("floors to zero", ex.Message);
    }

    [Fact]
    public void Every_problem_in_the_file_is_reported_at_once()
    {
        // One fix pass rather than one restart per mistake — the same contract jobs.json and items.json
        // already give a content author.
        WriteProperties("crime", new
        {
            packId = "",
            version = 1,
            properties = new[]
            {
                ValidProperty(id: "", name: "", levelRequirement: 0, price: -5, perHour: 0, capMinutes: 0)
            }
        });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));

        Assert.Contains("packId", ex.Message);
        Assert.Contains("'id'", ex.Message);
        Assert.Contains("name", ex.Message);
        Assert.Contains("levelRequirement", ex.Message);
        Assert.Contains("price", ex.Message);
        Assert.Contains("income.perHour", ex.Message);
        Assert.Contains("income.capMinutes", ex.Message);
    }

    [Fact]
    public void A_missing_income_block_is_reported_as_a_zero_rate_rather_than_crashing()
    {
        WriteRawProperties("crime", """
        {
          "packId": "crime", "version": 1,
          "properties": [
            { "id": "street-corner", "name": "Street Corner", "levelRequirement": 1, "price": 250 }
          ]
        }
        """);

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadProperties("crime"));
        Assert.Contains("income.perHour", ex.Message);
    }

    // -------------------------------------------------------------------------
    // The shipped crime pack — balance properties CP3 depends on
    // -------------------------------------------------------------------------

    [Fact]
    public void The_crime_pack_loads()
    {
        Assert.NotEmpty(new ContentPackLoader(FindRepoContentRoot()).LoadProperties("crime").Properties);
    }

    [Fact]
    public void Some_level_one_property_is_reachable_from_a_single_full_energy_bar_of_the_cheapest_job()
    {
        // CP3 asks a tester to buy a property and watch it earn. If the cheapest one needed a long grind
        // first, the checkpoint would not be reachable in one sitting. 1000 cash is roughly two full
        // energy bars of the starting job; anything at or under that is a short grind, not a project.
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadProperties("crime");

        Assert.Contains(catalog.Properties, p => p.LevelRequirement == 1 && p.Price <= 1000);
    }

    [Fact]
    public void The_cheapest_level_one_property_fills_up_fast_enough_to_watch()
    {
        // "Watch income accrue to cap" has to be observable inside a test session, not overnight.
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadProperties("crime");

        var starter = catalog.Properties
            .Where(p => p.LevelRequirement == 1)
            .OrderBy(p => p.Price)
            .First();

        Assert.True(starter.AccrualCapMinutes <= 30,
            $"Starter property '{starter.Id}' takes {starter.AccrualCapMinutes} minutes to fill.");
    }

    [Fact]
    public void The_starter_property_earns_a_visible_coin_within_a_minute()
    {
        // A rate below one coin a minute makes the CP3 screen look frozen for the first few minutes.
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadProperties("crime");

        var starter = catalog.Properties
            .Where(p => p.LevelRequirement == 1)
            .OrderBy(p => p.Price)
            .First();

        Assert.True(starter.IncomePerHour >= 60,
            $"Starter property '{starter.Id}' earns {starter.IncomePerHour}/hour — under a coin a minute.");
    }

    [Fact]
    public void No_property_is_free()
    {
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadProperties("crime");

        Assert.All(catalog.Properties, p => Assert.True(p.Price > 0, $"Property '{p.Id}' is free."));
    }

    [Fact]
    public void Every_property_pays_back_its_price_eventually()
    {
        // A property whose lifetime income never repays the purchase is a trap rather than a choice.
        // Full collections needed to break even is the honest measure, since income stops at the cap.
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadProperties("crime");

        Assert.All(catalog.Properties, p =>
            Assert.True(p.CapPerUnit > 0 && p.Price / p.CapPerUnit <= 200,
                $"Property '{p.Id}' needs {p.Price / Math.Max(p.CapPerUnit, 1)} full collections to break even."));
    }

    /// <summary>
    /// Walks up from the test assembly to the repo root's <c>content</c> folder, the same approach
    /// <see cref="ItemCatalogLoaderTests"/> uses — the test project does not copy content to output.
    /// </summary>
    private static string FindRepoContentRoot()
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
