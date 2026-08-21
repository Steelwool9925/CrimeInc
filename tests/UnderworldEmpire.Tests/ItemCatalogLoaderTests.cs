using System.Text.Json;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="ContentPackLoader.LoadItems"/> and <see cref="ItemCatalog"/> (T3.1).
/// Mirrors <see cref="JobCatalogLoaderTests"/>: each test writes a temp items.json — no web host and no
/// database required.
/// </summary>
public class ItemCatalogLoaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ue-items-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void WriteItems(string packId, object payload)
    {
        var dir = Path.Combine(_tempRoot, packId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "items.json"), JsonSerializer.Serialize(payload));
    }

    private void WriteRawItems(string packId, string rawJson)
    {
        var dir = Path.Combine(_tempRoot, packId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "items.json"), rawJson);
    }

    private ContentPackLoader Loader() => new(_tempRoot);

    /// <summary>A minimal valid catalog; individual tests override the parts they care about.</summary>
    private static object ValidCatalog(params object[] items) => new
    {
        packId = "crime",
        version = 1,
        items = items.Length > 0 ? items : [ValidItem()]
    };

    private static object ValidItem(
        string id = "brass-knuckles",
        string name = "Brass Knuckles",
        string slot = "weapon",
        int levelRequirement = 1,
        long price = 75,
        int attack = 4,
        int defense = 0) => new
        {
            id,
            name,
            description = "d",
            slot,
            levelRequirement,
            price,
            stats = new { attack, defense }
        };

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public void Valid_catalog_loads_items()
    {
        WriteItems("crime", ValidCatalog());

        var catalog = Loader().LoadItems("crime");

        var item = Assert.Single(catalog.Items);
        Assert.Equal("brass-knuckles", item.Id);
        Assert.Equal("Brass Knuckles", item.Name);
        Assert.Equal("d", item.Description);
        Assert.Equal(ItemSlot.Weapon, item.Slot);
        Assert.Equal(1, item.LevelRequirement);
        Assert.Equal(75, item.Price);
        Assert.Equal(4, item.Attack);
        Assert.Equal(0, item.Defense);
    }

    [Fact]
    public void Items_keep_the_order_content_declared()
    {
        WriteItems("crime", ValidCatalog(
            ValidItem(id: "second"),
            ValidItem(id: "first")));

        var catalog = Loader().LoadItems("crime");

        Assert.Equal(["second", "first"], catalog.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData("weapon", ItemSlot.Weapon)]
    [InlineData("armor", ItemSlot.Armor)]
    [InlineData("vehicle", ItemSlot.Vehicle)]
    [InlineData("Weapon", ItemSlot.Weapon)]
    [InlineData("VEHICLE", ItemSlot.Vehicle)]
    public void Slot_names_are_parsed_case_insensitively(string slot, ItemSlot expected)
    {
        // Unlike an item id, a slot name is a fixed engine vocabulary rather than a content key, so
        // casing in the pack file is a style choice and must not change the meaning.
        WriteItems("crime", ValidCatalog(ValidItem(slot: slot)));

        var catalog = Loader().LoadItems("crime");

        Assert.Equal(expected, catalog.Items[0].Slot);
    }

    [Fact]
    public void Unknown_fields_are_ignored_so_a_newer_pack_stays_loadable()
    {
        WriteRawItems("crime", """
            {
              "packId": "crime",
              "version": 2,
              "tradingEnabled": true,
              "items": [
                {
                  "id": "brass-knuckles",
                  "name": "Brass Knuckles",
                  "slot": "weapon",
                  "levelRequirement": 1,
                  "price": 75,
                  "stats": { "attack": 4, "defense": 0, "luck": 3 }
                }
              ]
            }
            """);

        var catalog = Loader().LoadItems("crime");

        Assert.Single(catalog.Items);
    }

    // -------------------------------------------------------------------------
    // Lookup
    // -------------------------------------------------------------------------

    [Fact]
    public void An_item_can_be_found_by_its_content_id()
    {
        WriteItems("crime", ValidCatalog());

        var catalog = Loader().LoadItems("crime");

        Assert.Equal("brass-knuckles", catalog.Find("brass-knuckles")?.Id);
    }

    [Fact]
    public void An_unknown_id_finds_nothing()
    {
        WriteItems("crime", ValidCatalog());

        Assert.Null(Loader().LoadItems("crime").Find("no-such-item"));
    }

    [Fact]
    public void Ids_are_matched_ordinally()
    {
        // Ids are content keys, not user text. A case-insensitive match would silently admit a second
        // spelling of every id — the same rule the job catalog applies.
        WriteItems("crime", ValidCatalog());

        Assert.Null(Loader().LoadItems("crime").Find("BRASS-KNUCKLES"));
    }

    // -------------------------------------------------------------------------
    // File-level failures
    // -------------------------------------------------------------------------

    [Fact]
    public void A_missing_file_is_reported_with_its_path()
    {
        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("items.json", ex.Message);
    }

    [Fact]
    public void Malformed_json_is_reported_as_a_content_error()
    {
        WriteRawItems("crime", "{ not json");

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void A_pack_id_mismatch_is_reported()
    {
        WriteItems("crime", new { packId = "noir", version = 1, items = new[] { ValidItem() } });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("'packId' mismatch", ex.Message);
    }

    [Fact]
    public void An_empty_catalog_is_reported()
    {
        WriteItems("crime", new { packId = "crime", version = 1, items = Array.Empty<object>() });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("'items' is missing or empty", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Per-entry validation
    // -------------------------------------------------------------------------

    [Fact]
    public void A_blank_id_is_reported_by_array_index()
    {
        WriteItems("crime", ValidCatalog(ValidItem(), ValidItem(id: "")));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("items[1]", ex.Message);
    }

    [Fact]
    public void A_duplicate_id_is_reported()
    {
        WriteItems("crime", ValidCatalog(ValidItem(), ValidItem()));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("duplicate 'id'", ex.Message);
    }

    [Fact]
    public void An_id_too_long_for_the_inventory_column_is_reported()
    {
        // PlayerItem.ItemId is varchar(64). Without this check a 70-character id passes startup and
        // then fails as a 500 on the first purchase, when Postgres rejects the insert.
        WriteItems("crime", ValidCatalog(ValidItem(id: new string('x', 65))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("64", ex.Message);
    }

    [Fact]
    public void An_id_exactly_at_the_column_limit_is_accepted()
    {
        WriteItems("crime", ValidCatalog(ValidItem(id: new string('x', 64))));

        Assert.Single(Loader().LoadItems("crime").Items);
    }

    [Fact]
    public void A_blank_name_is_reported()
    {
        WriteItems("crime", ValidCatalog(ValidItem(name: "")));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("'name' is missing or empty", ex.Message);
    }

    [Fact]
    public void An_unknown_slot_is_reported_and_lists_the_slots_that_exist()
    {
        WriteItems("crime", ValidCatalog(ValidItem(slot: "hat")));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("'slot'", ex.Message);
        Assert.Contains("weapon", ex.Message);
    }

    [Fact]
    public void A_missing_slot_is_reported_rather_than_defaulted()
    {
        // Defaulting to Weapon would silently mis-slot an item and break the one-per-slot rule in a way
        // no error message would ever mention.
        WriteRawItems("crime", """
            {
              "packId": "crime",
              "version": 1,
              "items": [
                { "id": "mystery", "name": "Mystery", "levelRequirement": 1, "price": 10 }
              ]
            }
            """);

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("'slot'", ex.Message);
    }

    [Fact]
    public void A_level_requirement_below_one_is_reported()
    {
        WriteItems("crime", ValidCatalog(ValidItem(levelRequirement: 0)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("'levelRequirement'", ex.Message);
    }

    [Fact]
    public void A_negative_price_is_reported()
    {
        WriteItems("crime", ValidCatalog(ValidItem(price: -1)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("'price'", ex.Message);
    }

    [Fact]
    public void A_negative_stat_bonus_is_reported()
    {
        WriteItems("crime", ValidCatalog(ValidItem(attack: -2, defense: -3)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("'stats.attack'", ex.Message);
        Assert.Contains("'stats.defense'", ex.Message);
    }

    [Fact]
    public void Every_problem_is_reported_in_one_message()
    {
        // The loader must not stop at the first problem — a pack author fixing content should see all of
        // it in one pass rather than one error per restart.
        WriteItems("crime", new
        {
            packId = "noir",
            version = 1,
            items = new[]
            {
                ValidItem(id: "", name: ""),
                ValidItem(slot: "hat", levelRequirement: 0, price: -5)
            }
        });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadItems("crime"));

        Assert.Contains("'packId' mismatch", ex.Message);
        Assert.Contains("items[0]", ex.Message);
        Assert.Contains("items[1]", ex.Message);
        Assert.Contains("'name' is missing or empty", ex.Message);
        Assert.Contains("'slot'", ex.Message);
        Assert.Contains("'levelRequirement'", ex.Message);
        Assert.Contains("'price'", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Real crime pack (from repo root content/crime)
    // -------------------------------------------------------------------------

    [Fact]
    public void Real_crime_catalog_loads_and_fills_every_slot()
    {
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadItems("crime");

        Assert.NotEmpty(catalog.Items);

        // The store and inventory screens (T3.5) group by slot; an empty slot would render as a dead
        // section, and combat (M4) would have a permanently unfillable gap.
        foreach (var slot in Enum.GetValues<ItemSlot>())
            Assert.Contains(catalog.Items, i => i.Slot == slot);
    }

    [Fact]
    public void Real_crime_catalog_lets_a_fresh_player_buy_and_feel_their_first_item()
    {
        // CP3 asks the tester to buy + equip a weapon and watch Attack/Defense rise, in one sitting.
        //
        // That used to mean "some level-1 item costs no more than the starting cash". A new player
        // now starts with nothing, so the honest version of the property is that the starter item is
        // reachable on one full energy bar of the pack's first job — measured at that job's cash
        // floor and its base success chance, so the figure is what an unlucky player can rely on
        // rather than what a lucky one might see.
        var loader = new ContentPackLoader(FindRepoContentRoot());
        var items = loader.LoadItems("crime");
        var jobs = loader.LoadJobs("crime");

        var firstJob = jobs.Jobs
            .Where(j => j.LevelRequirement == 1)
            .OrderBy(j => j.EnergyCost)
            .First();

        int attemptsOnAFullBar = PlayerDefaults.EnergyMax / firstJob.EnergyCost;
        double expectedCash =
            attemptsOnAFullBar * firstJob.CashMin * firstJob.BaseSuccessChance;

        var starter = items.Items
            .Where(i => i.LevelRequirement == 1 && i.Attack + i.Defense >= 1)
            .OrderBy(i => i.Price)
            .FirstOrDefault();

        Assert.NotNull(starter);
        Assert.True(starter.Price <= expectedCash + PlayerDefaults.StartingCash,
            $"Cheapest stat-moving level-1 item '{starter.Id}' costs {starter.Price}, but one energy " +
            $"bar of '{firstJob.Id}' only expects {expectedCash:0.##} cash on top of a starting " +
            $"{PlayerDefaults.StartingCash} — so CP3 would take more than one sitting to reach.");
    }

    [Fact]
    public void Real_crime_catalog_prices_every_item_above_zero()
    {
        // A free item would let a level-1 player fill a slot without engaging the economy at all.
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadItems("crime");

        Assert.All(catalog.Items, i => Assert.True(i.Price > 0, $"Item '{i.Id}' is free."));
    }

    // -------------------------------------------------------------------------
    // Resolving holdings against the catalog
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolving_equipped_holdings_returns_only_the_worn_ones()
    {
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadItems("crime");
        var playerId = Guid.NewGuid();

        var worn = new PlayerItem { PlayerId = playerId, ItemId = "brass-knuckles" };
        worn.AddCopies(1);
        worn.Equip();

        var spare = new PlayerItem { PlayerId = playerId, ItemId = "leather-jacket" };
        spare.AddCopies(1);

        var resolved = catalog.ResolveEquipped([worn, spare]);

        Assert.Equal(["brass-knuckles"], resolved.Select(i => i.Id));
    }

    [Fact]
    public void An_equipped_holding_whose_item_no_longer_exists_is_skipped_rather_than_throwing()
    {
        // Content dropped the item after the player bought it. The row stays — so a restored item comes
        // back with its history — but an item that no longer exists can no longer score anything, and a
        // throw here would lock the player out of their own inventory screen.
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadItems("crime");
        var ghost = new PlayerItem { PlayerId = Guid.NewGuid(), ItemId = "retired-item" };
        ghost.AddCopies(1);
        ghost.Equip();

        Assert.Empty(catalog.ResolveEquipped([ghost]));
    }

    /// <summary>
    /// Walks up from the test assembly to the repo root's <c>content</c> folder, the same approach
    /// <see cref="JobCatalogLoaderTests"/> uses — the test project does not copy content to output.
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
