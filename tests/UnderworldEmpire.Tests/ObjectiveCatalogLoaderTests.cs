using UnderworldEmpire.Domain.Daily;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>Loading and validating <c>objectives.json</c>, against the real job catalog.</summary>
public class ObjectiveCatalogLoaderTests : IDisposable
{
    private const string PackId = "crime";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "objective-loader-" + Guid.NewGuid().ToString("N"));
    private readonly JobCatalog _jobs;

    public ObjectiveCatalogLoaderTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, PackId));
        var real = new ContentPackLoader(RepoContent.FindRoot());
        _jobs = real.LoadJobs(PackId, real.LoadItems(PackId));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ObjectiveCatalog Load(string objectivesJson)
    {
        File.WriteAllText(Path.Combine(_root, PackId, "objectives.json"), objectivesJson);
        return new ContentPackLoader(_root).LoadObjectives(PackId, _jobs);
    }

    private static string One(string extra = "", string type = "completeJobTimes", string target = "1", string minLevel = "1",
        string reward = "\"reward\": { \"cash\": 10, \"xp\": 0 }") =>
        $$"""
        { "packId": "crime", "version": 1, "objectives": [
          { "id": "x", "name": "X", "type": "{{type}}", "target": {{target}}, "minLevel": {{minLevel}}, {{reward}} {{extra}} }
        ] }
        """;

    private string Errors(string json) =>
        Assert.Throws<ContentPackException>(() => Load(json)).Message;

    [Fact]
    public void The_shipped_pool_loads()
    {
        var catalog = new ContentPackLoader(RepoContent.FindRoot()).LoadObjectives(PackId, _jobs);

        Assert.NotEmpty(catalog.Objectives);
        Assert.NotNull(catalog.Find(catalog.Objectives[0].Id));
        Assert.Null(catalog.Find("no-such-objective"));
    }

    [Fact]
    public void A_valid_objective_round_trips()
    {
        var o = Load(One(extra: ", \"jobId\": \"shake-down-corner-store\"")).Objectives.Single();

        Assert.Equal(ObjectiveType.CompleteJobTimes, o.Type);
        Assert.Equal("shake-down-corner-store", o.JobId);
        Assert.Equal(10, o.RewardCash);
    }

    [Fact]
    public void An_unknown_type_is_named()
    {
        Assert.Contains("'type' must be one of", Errors(One(type: "dance")));
    }

    [Fact]
    public void A_job_that_does_not_exist_is_rejected()
    {
        Assert.Contains("is not a job in this pack", Errors(One(extra: ", \"jobId\": \"nope\"")));
    }

    [Fact]
    public void Offered_below_the_jobs_own_level_gate_is_rejected()
    {
        // rob-an-armored-transport needs level 8.
        Assert.Contains("is below 'rob-an-armored-transport' level requirement", Errors(One(extra: ", \"jobId\": \"rob-an-armored-transport\"", minLevel: "2")));
    }

    [Fact]
    public void A_job_id_on_a_type_that_ignores_it_is_rejected()
    {
        Assert.Contains("only applies to completeJobTimes", Errors(One(type: "earnCash", extra: ", \"jobId\": \"shake-down-corner-store\"")));
    }

    [Fact]
    public void A_missing_reward_block_is_a_named_error_not_a_free_objective()
    {
        Assert.Contains("'reward' block is missing", Errors(One(reward: "\"x\": 0")));
    }

    [Fact]
    public void An_objective_that_pays_nothing_is_rejected()
    {
        Assert.Contains("'reward' must pay cash or xp", Errors(One(reward: "\"reward\": { \"cash\": 0, \"xp\": 0 }")));
    }

    [Fact]
    public void A_zero_target_is_rejected()
    {
        Assert.Contains("'target' must be >= 1", Errors(One(target: "0")));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var message = Errors(One(type: "dance", target: "0", minLevel: "0"));

        Assert.Contains("'type'", message);
        Assert.Contains("'target'", message);
        Assert.Contains("'minLevel'", message);
    }

    [Fact]
    public void A_missing_file_is_a_content_error()
    {
        Assert.Throws<ContentPackException>(() => new ContentPackLoader(_root).LoadObjectives(PackId, _jobs));
    }
}
