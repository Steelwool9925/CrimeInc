using System.Text.Json;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="ContentPackLoader.LoadJobs"/> and <see cref="JobCatalog"/> (T2.1).
/// Mirrors <see cref="ContentPackLoaderTests"/>: each test writes a temp jobs.json — no web host and
/// no database required.
/// </summary>
public class JobCatalogLoaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"ue-jobs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void WriteJobs(string packId, object payload)
    {
        var dir = Path.Combine(_tempRoot, packId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "jobs.json"), JsonSerializer.Serialize(payload));
    }

    private void WriteRawJobs(string packId, string rawJson)
    {
        var dir = Path.Combine(_tempRoot, packId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "jobs.json"), rawJson);
    }

    private ContentPackLoader Loader() => new(_tempRoot);

    /// <summary>A minimal valid catalog; individual tests override the parts they care about.</summary>
    /// <remarks>
    /// Built as a dictionary rather than an anonymous type since T3R.3, because the <c>risk</c> block
    /// is required and several tests need it <b>absent</b> rather than merely wrong — an anonymous
    /// type cannot omit a property conditionally.
    /// </remarks>
    private static Dictionary<string, object?> ValidCatalog(params object[] jobs) =>
        CatalogWith(jobs: jobs);

    private static Dictionary<string, object?> CatalogWith(
        string packId = "crime",
        object? mastery = null,
        object? risk = null,
        bool omitRisk = false,
        params object[] jobs)
    {
        var catalog = new Dictionary<string, object?>
        {
            ["packId"] = packId,
            ["version"] = 2,
            ["mastery"] = mastery ?? new { completionsPerRank = 8, maxRank = 5 },
            ["jobs"] = jobs.Length > 0 ? jobs : [ValidJob()]
        };

        if (!omitRisk)
            catalog["risk"] = risk ?? ValidRiskRules();

        return catalog;
    }

    /// <summary>Pack-wide risk rules in their valid shape.</summary>
    private static object ValidRiskRules(
        double maxSuccessChance = 0.95,
        double consolationCashFraction = 0.01,
        double consolationXpFraction = 0.10) =>
        new { maxSuccessChance, consolationCashFraction, consolationXpFraction };

    private static object ValidRequirements(int attack = 0, int defense = 0) =>
        new { attack, defense };

    private static object ValidJobRisk(
        double baseSuccessChance = 0.85,
        double successChancePerSurplusPoint = 0.01,
        double itemSuccessChancePerSurplusPoint = 0.0,
        int healthLossMin = 0,
        int healthLossMax = 1) =>
        new
        {
            baseSuccessChance,
            successChancePerSurplusPoint,
            itemSuccessChancePerSurplusPoint,
            healthLossMin,
            healthLossMax
        };

    private static object ValidItemRequirement(string itemId = "switchblade", int minQuantity = 2) =>
        new { itemId, minQuantity };

    private static Dictionary<string, object?> ValidJob(
        string id = "mug-a-tourist",
        string name = "Mug a Tourist",
        int energyCost = 1,
        int levelRequirement = 1,
        long cashMin = 10,
        long cashMax = 20,
        long xp = 5,
        object? requirements = null,
        object? risk = null,
        bool omitRequirements = false,
        bool omitRisk = false,
        List<object>? itemRequirements = null)
    {
        var job = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["name"] = name,
            ["description"] = "d",
            ["area"] = "downtown",
            ["energyCost"] = energyCost,
            ["levelRequirement"] = levelRequirement,
            ["payout"] = new { cashMin, cashMax, xp }
        };

        if (!omitRequirements)
            job["requirements"] = requirements ?? ValidRequirements();
        if (!omitRisk)
            job["risk"] = risk ?? ValidJobRisk();
        if (itemRequirements is not null)
            job["itemRequirements"] = itemRequirements;

        return job;
    }

    /// <summary>An item catalog with one weapon (id "switchblade") and one vehicle, for cross-catalog tests.</summary>
    private static ItemCatalog TestItemCatalog() => new([
        new Item
        {
            Id = "switchblade", Name = "Switchblade", Slot = ItemSlot.Weapon,
            LevelRequirement = 1, Price = 10, Attack = 1, Defense = 0
        },
        new Item
        {
            Id = "stolen-sedan", Name = "Stolen Sedan", Slot = ItemSlot.Vehicle,
            LevelRequirement = 1, Price = 10, Attack = 1, Defense = 0
        }
    ]);

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public void Valid_catalog_loads_jobs_and_mastery_rules()
    {
        WriteJobs("crime", ValidCatalog());

        var catalog = Loader().LoadJobs("crime");

        Assert.Single(catalog.Jobs);
        Assert.Equal(8, catalog.CompletionsPerRank);
        Assert.Equal(5, catalog.MaxRank);

        var job = catalog.Jobs[0];
        Assert.Equal("mug-a-tourist", job.Id);
        Assert.Equal("Mug a Tourist", job.Name);
        Assert.Equal("downtown", job.Area);
        Assert.Equal(1, job.EnergyCost);
        Assert.Equal(1, job.LevelRequirement);
        Assert.Equal(10, job.CashMin);
        Assert.Equal(20, job.CashMax);
        Assert.Equal(5, job.Xp);
    }

    [Fact]
    public void Jobs_are_exposed_in_content_declared_order()
    {
        WriteJobs("crime", ValidCatalog(
            ValidJob(id: "first"),
            ValidJob(id: "second"),
            ValidJob(id: "third")));

        var catalog = Loader().LoadJobs("crime");

        Assert.Equal(["first", "second", "third"], catalog.Jobs.Select(j => j.Id));
    }

    [Fact]
    public void Find_resolves_a_job_by_id()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(id: "boost-a-car", name: "Boost a Car")));

        var catalog = Loader().LoadJobs("crime");

        Assert.Equal("Boost a Car", catalog.Find("boost-a-car")!.Name);
    }

    [Fact]
    public void Find_returns_null_for_an_unknown_id()
    {
        WriteJobs("crime", ValidCatalog());

        Assert.Null(Loader().LoadJobs("crime").Find("no-such-job"));
    }

    [Fact]
    public void Find_is_case_sensitive()
    {
        // Ids are content keys, not user text. A case-insensitive match would silently admit a
        // second spelling of every id and let /jobs/MUG-A-TOURIST resolve.
        WriteJobs("crime", ValidCatalog(ValidJob(id: "mug-a-tourist")));

        var catalog = Loader().LoadJobs("crime");

        Assert.NotNull(catalog.Find("mug-a-tourist"));
        Assert.Null(catalog.Find("MUG-A-TOURIST"));
    }

    [Fact]
    public void Unknown_fields_are_tolerated()
    {
        WriteRawJobs("crime", """
        {
          "packId": "crime",
          "version": 1,
          "futureField": { "nested": true },
          "mastery": { "completionsPerRank": 8, "maxRank": 5 },
          "risk": {
            "maxSuccessChance": 0.95,
            "consolationCashFraction": 0.01,
            "consolationXpFraction": 0.10
          },
          "jobs": [
            {
              "id": "mug-a-tourist",
              "name": "Mug a Tourist",
              "energyCost": 1,
              "levelRequirement": 1,
              "unknownJobField": 7,
              "payout": { "cashMin": 10, "cashMax": 20, "xp": 5 },
              "requirements": { "attack": 0, "defense": 0 },
              "risk": {
                "baseSuccessChance": 0.85,
                "successChancePerSurplusPoint": 0.01,
                "healthLossMin": 0,
                "healthLossMax": 1
              }
            }
          ]
        }
        """);

        var catalog = Loader().LoadJobs("crime");

        Assert.Single(catalog.Jobs);
    }

    // -------------------------------------------------------------------------
    // Failure modes
    // -------------------------------------------------------------------------

    [Fact]
    public void Missing_file_throws_ContentPackException()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "crime"));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void Invalid_json_throws_ContentPackException()
    {
        WriteRawJobs("crime", "{ this is not json ");

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void PackId_mismatch_throws_ContentPackException()
    {
        WriteJobs("crime", new
        {
            packId = "fantasy",
            mastery = new { completionsPerRank = 10, maxRank = 5 },
            jobs = new[] { ValidJob() }
        });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'packId' mismatch", ex.Message);
    }

    [Fact]
    public void Empty_jobs_array_throws_ContentPackException()
    {
        WriteJobs("crime", new
        {
            packId = "crime",
            mastery = new { completionsPerRank = 10, maxRank = 5 },
            jobs = Array.Empty<object>()
        });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("at least one job", ex.Message);
    }

    [Fact]
    public void Duplicate_job_id_throws_ContentPackException()
    {
        WriteJobs("crime", ValidCatalog(
            ValidJob(id: "same"),
            ValidJob(id: "same")));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("duplicate 'id'", ex.Message);
    }

    [Fact]
    public void Zero_energy_cost_throws_ContentPackException()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(energyCost: 0)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'energyCost' must be >= 1", ex.Message);
    }

    [Fact]
    public void CashMax_below_cashMin_throws_ContentPackException()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(cashMin: 100, cashMax: 50)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("below 'payout.cashMin'", ex.Message);
    }

    [Fact]
    public void Zero_completions_per_rank_throws_ContentPackException()
    {
        // completionsPerRank is a divisor in the rank calculation, so 0 must never reach the domain.
        WriteJobs("crime", new
        {
            packId = "crime",
            mastery = new { completionsPerRank = 0, maxRank = 5 },
            jobs = new[] { ValidJob() }
        });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'mastery.completionsPerRank' must be >= 1", ex.Message);
    }

    [Fact]
    public void All_validation_errors_are_reported_together()
    {
        // The loader must not stop at the first problem — a pack author fixing content should see
        // every error in one pass rather than one per restart.
        WriteJobs("crime", new
        {
            packId = "wrong",
            mastery = new { completionsPerRank = 0, maxRank = -1 },
            jobs = new[] { ValidJob(id: "", name: "", energyCost: 0, levelRequirement: 0) }
        });

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));

        Assert.Contains("'packId' mismatch", ex.Message);
        Assert.Contains("'mastery.completionsPerRank' must be >= 1", ex.Message);
        Assert.Contains("'mastery.maxRank' must be >= 0", ex.Message);
        Assert.Contains("'id' is missing or empty", ex.Message);
        Assert.Contains("'name' is missing or empty", ex.Message);
        Assert.Contains("'energyCost' must be >= 1", ex.Message);
        Assert.Contains("'levelRequirement' must be >= 1", ex.Message);
    }

    [Fact]
    public void Error_for_a_blank_id_names_the_array_index()
    {
        // "the job with no id" is useless without knowing which slot it occupies.
        WriteJobs("crime", ValidCatalog(
            ValidJob(id: "fine"),
            ValidJob(id: "")));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("jobs[1]", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Real crime pack (from repo root content/crime)
    // -------------------------------------------------------------------------

    [Fact]
    public void Real_crime_catalog_loads_and_is_reachable_for_a_new_player()
    {
        var catalog = new ContentPackLoader(RepoContent.FindRoot()).LoadJobs("crime");

        Assert.NotEmpty(catalog.Jobs);

        // A brand-new player must be able to attempt something immediately: at least one job has to
        // sit at level 1 and cost no more than the starting energy maximum. Without this a fresh
        // install would show an entirely locked job list.
        Assert.Contains(catalog.Jobs, j =>
            j.LevelRequirement == 1 && j.EnergyCost <= PlayerDefaults.EnergyMax);

        // Every job must be individually affordable on a full starting bar or it is unreachable
        // until the energy maximum grows, which nothing in the MVP does yet.
        Assert.All(catalog.Jobs, j => Assert.True(
            j.EnergyCost <= PlayerDefaults.EnergyMax,
            $"Job '{j.Id}' costs {j.EnergyCost} energy but the starting maximum is {PlayerDefaults.EnergyMax}."));
    }

    // -------------------------------------------------------------------------
    // Risk rules and requirements (T3R.3)
    // -------------------------------------------------------------------------

    [Fact]
    public void Valid_catalog_exposes_the_pack_wide_risk_rules()
    {
        WriteJobs("crime", ValidCatalog());

        var catalog = Loader().LoadJobs("crime");

        Assert.Equal(0.95, catalog.MaxSuccessChance, precision: 10);
        Assert.Equal(0.01, catalog.ConsolationCashFraction, precision: 10);
        Assert.Equal(0.10, catalog.ConsolationXpFraction, precision: 10);
    }

    [Fact]
    public void Loaded_jobs_carry_their_requirements_and_risk()
    {
        WriteJobs("crime", CatalogWith(jobs: ValidJob(
            requirements: ValidRequirements(attack: 6, defense: 4),
            risk: ValidJobRisk(
                baseSuccessChance: 0.7,
                successChancePerSurplusPoint: 0.02,
                healthLossMin: 2,
                healthLossMax: 9))));

        var job = Loader().LoadJobs("crime").Jobs.Single();

        Assert.Equal(6, job.AttackRequirement);
        Assert.Equal(4, job.DefenseRequirement);
        Assert.Equal(0.7, job.BaseSuccessChance, precision: 10);
        Assert.Equal(0.02, job.SuccessChancePerSurplusPoint, precision: 10);
        Assert.Equal(2, job.HealthLossMin);
        Assert.Equal(9, job.HealthLossMax);
    }

    [Fact]
    public void A_missing_pack_risk_block_is_a_named_error()
    {
        // The JSON models are nullable precisely so this is reportable. Defaulted to zeros it would
        // read as "every job is impossible" — a silent catastrophe rather than a startup message.
        WriteJobs("crime", CatalogWith(omitRisk: true));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'risk' is missing", ex.Message);
    }

    [Fact]
    public void A_missing_job_requirements_block_is_a_named_error()
    {
        // Absent, this would read as "ungated" — which is exactly what a content author who forgot
        // the block would fail to notice.
        WriteJobs("crime", ValidCatalog(ValidJob(omitRequirements: true)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'requirements' is missing", ex.Message);
    }

    [Fact]
    public void A_missing_job_risk_block_is_a_named_error()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(omitRisk: true)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'risk' is missing", ex.Message);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void An_out_of_range_max_success_chance_throws(double maxSuccessChance)
    {
        // Zero is rejected as well as negatives: a pack whose ceiling is zero has made every job
        // impossible, which is never what was meant.
        WriteJobs("crime", CatalogWith(risk: ValidRiskRules(maxSuccessChance: maxSuccessChance)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'risk.maxSuccessChance'", ex.Message);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void An_out_of_range_consolation_cash_fraction_throws(double fraction)
    {
        WriteJobs("crime", CatalogWith(risk: ValidRiskRules(consolationCashFraction: fraction)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'risk.consolationCashFraction'", ex.Message);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void An_out_of_range_consolation_xp_fraction_throws(double fraction)
    {
        WriteJobs("crime", CatalogWith(risk: ValidRiskRules(consolationXpFraction: fraction)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'risk.consolationXpFraction'", ex.Message);
    }

    [Fact]
    public void A_negative_attack_requirement_throws()
    {
        WriteJobs("crime", ValidCatalog(
            ValidJob(requirements: ValidRequirements(attack: -1))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'requirements.attack' must be >= 0", ex.Message);
    }

    [Fact]
    public void A_negative_defense_requirement_throws()
    {
        WriteJobs("crime", ValidCatalog(
            ValidJob(requirements: ValidRequirements(defense: -1))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'requirements.defense' must be >= 0", ex.Message);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void An_out_of_range_base_success_chance_throws(double chance)
    {
        WriteJobs("crime", ValidCatalog(
            ValidJob(risk: ValidJobRisk(baseSuccessChance: chance))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'risk.baseSuccessChance'", ex.Message);
    }

    [Fact]
    public void A_base_success_chance_above_the_pack_ceiling_throws()
    {
        // Job.SuccessChanceFor throws on this contradiction at roll time. Catching it at load turns
        // a request-time crash into a startup message naming the job.
        WriteJobs("crime", CatalogWith(
            risk: ValidRiskRules(maxSuccessChance: 0.90),
            jobs: ValidJob(risk: ValidJobRisk(baseSuccessChance: 0.99))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("above 'risk.maxSuccessChance'", ex.Message);
        Assert.Contains("mug-a-tourist", ex.Message);
    }

    [Fact]
    public void A_negative_per_surplus_point_gain_throws()
    {
        WriteJobs("crime", ValidCatalog(
            ValidJob(risk: ValidJobRisk(successChancePerSurplusPoint: -0.01))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'risk.successChancePerSurplusPoint' must be >= 0", ex.Message);
    }

    [Fact]
    public void A_negative_minimum_health_loss_throws()
    {
        WriteJobs("crime", ValidCatalog(
            ValidJob(risk: ValidJobRisk(healthLossMin: -1, healthLossMax: 3))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("'risk.healthLossMin' must be >= 0", ex.Message);
    }

    [Fact]
    public void A_maximum_health_loss_below_the_minimum_throws()
    {
        WriteJobs("crime", ValidCatalog(
            ValidJob(risk: ValidJobRisk(healthLossMin: 5, healthLossMax: 3))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));
        Assert.Contains("is below 'risk.healthLossMin'", ex.Message);
    }

    [Fact]
    public void Risk_errors_are_reported_alongside_every_other_error()
    {
        // Same contract as the rest of the loader: one fix pass, not one restart per error.
        WriteJobs("crime", CatalogWith(
            packId: "wrong",
            omitRisk: true,
            jobs: ValidJob(id: "", omitRequirements: true, omitRisk: true)));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));

        Assert.Contains("'packId' mismatch", ex.Message);
        Assert.Contains("'risk' is missing", ex.Message);
        Assert.Contains("'requirements' is missing", ex.Message);
        Assert.Contains("'id' is missing or empty", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Item requirements (Tier 1 consumable item gate) — the one cross-catalog check
    // -------------------------------------------------------------------------

    [Fact]
    public void A_job_with_no_item_requirements_field_loads_ungated_by_items()
    {
        // Absent is the ordinary case here — every job written before this field existed has none.
        WriteJobs("crime", ValidCatalog());

        var job = Loader().LoadJobs("crime", TestItemCatalog()).Jobs.Single();

        Assert.Empty(job.ItemRequirements);
        Assert.Equal(0.0, job.ItemSuccessChancePerSurplusPoint, precision: 10);
    }

    [Fact]
    public void Loaded_jobs_carry_their_item_requirements_and_item_surplus_rate()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [ValidItemRequirement(itemId: "switchblade", minQuantity: 3)],
            risk: ValidJobRisk(itemSuccessChancePerSurplusPoint: 0.02))));

        var job = Loader().LoadJobs("crime", TestItemCatalog()).Jobs.Single();

        var requirement = Assert.Single(job.ItemRequirements);
        Assert.Equal("switchblade", requirement.ItemId);
        Assert.Equal(3, requirement.MinQuantity);
        Assert.Equal(0.02, job.ItemSuccessChancePerSurplusPoint, precision: 10);
    }

    [Fact]
    public void An_item_requirement_naming_an_unknown_item_id_throws()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [ValidItemRequirement(itemId: "no-such-item")])));

        var ex = Assert.Throws<ContentPackException>(
            () => Loader().LoadJobs("crime", TestItemCatalog()));

        Assert.Contains("unknown item id 'no-such-item'", ex.Message);
    }

    [Fact]
    public void An_item_requirement_naming_a_vehicle_throws()
    {
        // Only weapon/armor items can gate a job — a vehicle is a different kind of gear entirely.
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [ValidItemRequirement(itemId: "stolen-sedan")])));

        var ex = Assert.Throws<ContentPackException>(
            () => Loader().LoadJobs("crime", TestItemCatalog()));

        Assert.Contains("'stolen-sedan', which is a vehicle", ex.Message);
    }

    [Fact]
    public void An_unknown_item_id_is_not_checked_when_no_item_catalog_is_supplied()
    {
        // LoadJobs's itemCatalog parameter is optional precisely so a caller with nothing to
        // validate against (most of this file's fixtures) is not forced to construct one.
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [ValidItemRequirement(itemId: "no-such-item")])));

        var job = Loader().LoadJobs("crime").Jobs.Single();

        Assert.Single(job.ItemRequirements);
    }

    [Fact]
    public void An_item_requirement_with_a_blank_item_id_throws()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [ValidItemRequirement(itemId: "")])));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));

        Assert.Contains("missing or empty 'itemId'", ex.Message);
    }

    [Fact]
    public void A_requirement_that_omits_quantity_consumed_spends_one()
    {
        // The field is additive: every pack written before it existed must load unchanged.
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [ValidItemRequirement(minQuantity: 3)])));

        var requirement = Assert.Single(Loader().LoadJobs("crime", TestItemCatalog()).Jobs.Single().ItemRequirements);

        Assert.Equal(1, requirement.QuantityConsumed);
    }

    [Fact]
    public void A_declared_quantity_consumed_is_carried_through()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [new { itemId = "switchblade", minQuantity = 3, quantityConsumed = 2 }])));

        var requirement = Assert.Single(Loader().LoadJobs("crime", TestItemCatalog()).Jobs.Single().ItemRequirements);

        Assert.Equal(3, requirement.MinQuantity);
        Assert.Equal(2, requirement.QuantityConsumed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_quantity_consumed_outside_one_to_the_minimum_throws(int consumed)
    {
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [new { itemId = "switchblade", minQuantity = 2, quantityConsumed = consumed }])));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));

        Assert.Contains($"'quantityConsumed' {consumed}, must be between 1 and 'minQuantity' (2)", ex.Message);
    }

    [Fact]
    public void An_item_requirement_with_a_minimum_quantity_below_one_throws()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [ValidItemRequirement(minQuantity: 0)])));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));

        Assert.Contains("'minQuantity' 0, must be >= 1", ex.Message);
    }

    [Fact]
    public void A_duplicate_item_requirement_for_the_same_item_throws()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(
            itemRequirements: [ValidItemRequirement(), ValidItemRequirement()])));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));

        Assert.Contains("duplicate entry for 'switchblade'", ex.Message);
    }

    [Fact]
    public void A_negative_item_success_chance_per_surplus_point_throws()
    {
        WriteJobs("crime", ValidCatalog(ValidJob(
            risk: ValidJobRisk(itemSuccessChancePerSurplusPoint: -0.01))));

        var ex = Assert.Throws<ContentPackException>(() => Loader().LoadJobs("crime"));

        Assert.Contains("'risk.itemSuccessChancePerSurplusPoint' must be >= 0", ex.Message);
    }

    [Fact]
    public void Real_crime_catalog_validates_hijack_a_delivery_trucks_switchblade_requirement()
    {
        // Integration proof: the real jobs.json + items.json pairing loads and cross-validates
        // cleanly, and the demo content is wired the way the README documents it.
        var realLoader = new ContentPackLoader(RepoContent.FindRoot());
        var itemCatalog = realLoader.LoadItems("crime");

        var job = realLoader.LoadJobs("crime", itemCatalog).Jobs
            .Single(j => j.Id == "hijack-a-delivery-truck");

        var requirement = Assert.Single(job.ItemRequirements);
        Assert.Equal("switchblade", requirement.ItemId);
        // Hold one, spend one: the requirement matches what an attempt actually consumes.
        Assert.Equal(1, requirement.MinQuantity);
        Assert.True(job.ItemSuccessChancePerSurplusPoint > 0.0);
    }

    [Fact]
    public void Real_crime_catalog_lets_a_fresh_player_reach_a_mastery_rank_on_one_energy_bar()
    {
        // CP2 asks the tester to watch the mastery bar tick up. That is only observable in one
        // sitting if the cheapest level-1 job can be run enough times on a full starting bar to
        // cross a rank threshold. This test pins that tuning property so a later balance pass
        // cannot silently make the checkpoint untestable.
        //
        // Until M3R this counted *attempts*, which was the same thing — every attempt completed.
        // Now that attempts can fail and a failure does not advance mastery, counting attempts
        // would leave this test green while the property it names quietly became false. It counts
        // expected completions instead, against the job's base chance: the floor a player sitting
        // exactly on the requirements gets, not the better odds a geared one enjoys.
        var catalog = new ContentPackLoader(RepoContent.FindRoot()).LoadJobs("crime");

        var cheapest = catalog.Jobs
            .Where(j => j.LevelRequirement == 1)
            .OrderBy(j => j.EnergyCost)
            .First();

        int attemptsOnAFullBar = PlayerDefaults.EnergyMax / cheapest.EnergyCost;
        double expectedCompletions = attemptsOnAFullBar * cheapest.BaseSuccessChance;

        Assert.True(expectedCompletions >= catalog.CompletionsPerRank,
            $"Cheapest level-1 job '{cheapest.Id}' allows {attemptsOnAFullBar} attempts on a full " +
            $"energy bar, expected to yield {expectedCompletions:0.##} completions at its base " +
            $"chance of {cheapest.BaseSuccessChance}, but {catalog.CompletionsPerRank} completions " +
            $"are needed for a mastery rank.");
    }
}
