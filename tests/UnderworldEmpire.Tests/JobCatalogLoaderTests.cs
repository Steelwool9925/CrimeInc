using System.Text.Json;
using UnderworldEmpire.Domain;
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
    private static object ValidCatalog(params object[] jobs) => new
    {
        packId = "crime",
        version = 1,
        mastery = new { completionsPerRank = 10, maxRank = 5 },
        jobs = jobs.Length > 0 ? jobs : [ValidJob()]
    };

    private static object ValidJob(
        string id = "mug-a-tourist",
        string name = "Mug a Tourist",
        int energyCost = 1,
        int levelRequirement = 1,
        long cashMin = 10,
        long cashMax = 20,
        long xp = 5) => new
        {
            id,
            name,
            description = "d",
            area = "downtown",
            energyCost,
            levelRequirement,
            payout = new { cashMin, cashMax, xp }
        };

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public void Valid_catalog_loads_jobs_and_mastery_rules()
    {
        WriteJobs("crime", ValidCatalog());

        var catalog = Loader().LoadJobs("crime");

        Assert.Single(catalog.Jobs);
        Assert.Equal(10, catalog.CompletionsPerRank);
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
          "mastery": { "completionsPerRank": 10, "maxRank": 5 },
          "jobs": [
            {
              "id": "mug-a-tourist",
              "name": "Mug a Tourist",
              "energyCost": 1,
              "levelRequirement": 1,
              "unknownJobField": 7,
              "payout": { "cashMin": 10, "cashMax": 20, "xp": 5 }
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
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadJobs("crime");

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

    [Fact]
    public void Real_crime_catalog_lets_a_fresh_player_reach_a_mastery_rank_on_one_energy_bar()
    {
        // CP2 asks the tester to watch the mastery bar tick up. That is only observable in one
        // sitting if the cheapest level-1 job can be run enough times on a full starting bar to
        // cross a rank threshold. This test pins that tuning property so a later balance pass
        // cannot silently make the checkpoint untestable.
        var catalog = new ContentPackLoader(FindRepoContentRoot()).LoadJobs("crime");

        var cheapest = catalog.Jobs
            .Where(j => j.LevelRequirement == 1)
            .OrderBy(j => j.EnergyCost)
            .First();

        int runsOnAFullBar = PlayerDefaults.EnergyMax / cheapest.EnergyCost;

        Assert.True(runsOnAFullBar >= catalog.CompletionsPerRank,
            $"Cheapest level-1 job '{cheapest.Id}' allows {runsOnAFullBar} runs on a full energy bar, " +
            $"but {catalog.CompletionsPerRank} completions are needed for a mastery rank.");
    }

    /// <summary>
    /// Walks up from the test assembly to the repo root's <c>content</c> folder, the same approach
    /// <see cref="ContentPackLoaderTests"/> uses — the test project does not copy content to output.
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
