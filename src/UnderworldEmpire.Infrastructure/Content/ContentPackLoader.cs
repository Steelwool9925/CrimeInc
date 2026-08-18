using System.Text;
using System.Text.Json;
using UnderworldEmpire.Domain.Jobs;

namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Loads a content pack by id from disk and validates required fields.
/// Resolves packs from <c>&lt;contentRoot&gt;/&lt;packId&gt;/pack.json</c>.
/// The content root defaults to <c>AppContext.BaseDirectory/content</c> and can be
/// overridden via the <c>Content:Root</c> configuration key.
/// </summary>
public sealed class ContentPackLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // Tolerate extra fields the engine does not yet know about.
        UnknownTypeHandling = System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonElement,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly string _contentRoot;

    /// <param name="contentRoot">
    ///     Base directory that contains per-pack sub-folders. Defaults to
    ///     <c>AppContext.BaseDirectory/content</c> when <see langword="null"/> or empty.
    /// </param>
    public ContentPackLoader(string? contentRoot = null)
    {
        _contentRoot = string.IsNullOrWhiteSpace(contentRoot)
            ? Path.Combine(AppContext.BaseDirectory, "content")
            : contentRoot;
    }

    /// <summary>
    /// Loads and validates the pack with <paramref name="packId"/>.
    /// </summary>
    /// <exception cref="ContentPackException">
    ///     Thrown when the pack file is missing, cannot be parsed, or fails validation.
    /// </exception>
    public ContentPack Load(string packId)
    {
        var packFile = Path.Combine(_contentRoot, packId, "pack.json");

        if (!File.Exists(packFile))
            throw new ContentPackException(
                $"Content pack '{packId}' not found at '{packFile}'.");

        ContentPack pack;
        try
        {
            var json = File.ReadAllText(packFile, Encoding.UTF8);
            pack = JsonSerializer.Deserialize<ContentPack>(json, JsonOptions)
                   ?? throw new ContentPackException(
                       $"pack.json for '{packId}' deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new ContentPackException(
                $"pack.json for '{packId}' is not valid JSON: {ex.Message}", ex);
        }

        Validate(pack, packId);
        return pack;
    }

    /// <summary>
    /// Loads and validates the job catalog for <paramref name="packId"/> from
    /// <c>&lt;contentRoot&gt;/&lt;packId&gt;/jobs.json</c>.
    /// </summary>
    /// <remarks>
    /// Validation runs to completion and reports every problem at once, exactly as
    /// <see cref="Load"/> does — a pack author fixing content should see all of it in one pass
    /// rather than one error per restart. Domain objects are constructed only after validation
    /// passes, so <see cref="Job.EnsureValid"/> never fires mid-collection and truncates the report.
    /// </remarks>
    /// <exception cref="ContentPackException">
    ///     Thrown when the file is missing, cannot be parsed, or fails validation.
    /// </exception>
    public JobCatalog LoadJobs(string packId)
    {
        var jobsFile = Path.Combine(_contentRoot, packId, "jobs.json");

        if (!File.Exists(jobsFile))
            throw new ContentPackException(
                $"Job catalog for pack '{packId}' not found at '{jobsFile}'.");

        JobCatalogFile file;
        try
        {
            var json = File.ReadAllText(jobsFile, Encoding.UTF8);
            file = JsonSerializer.Deserialize<JobCatalogFile>(json, JsonOptions)
                   ?? throw new ContentPackException(
                       $"jobs.json for '{packId}' deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new ContentPackException(
                $"jobs.json for '{packId}' is not valid JSON: {ex.Message}", ex);
        }

        ValidateJobs(file, packId);

        var jobs = new List<Job>(file.Jobs.Count);
        foreach (var entry in file.Jobs)
        {
            var job = new Job
            {
                Id = entry.Id,
                Name = entry.Name,
                Description = entry.Description,
                Area = entry.Area,
                EnergyCost = entry.EnergyCost,
                LevelRequirement = entry.LevelRequirement,
                CashMin = entry.Payout.CashMin,
                CashMax = entry.Payout.CashMax,
                Xp = entry.Payout.Xp
            };
            job.EnsureValid();
            jobs.Add(job);
        }

        return new JobCatalog(jobs, file.Mastery.CompletionsPerRank, file.Mastery.MaxRank);
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    private static void Validate(ContentPack pack, string expectedId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(pack.Id))
            errors.Add("'id' is missing or empty");
        else if (!string.Equals(pack.Id, expectedId, StringComparison.Ordinal))
            errors.Add($"'id' mismatch: expected '{expectedId}', found '{pack.Id}'");

        if (string.IsNullOrWhiteSpace(pack.Name))
            errors.Add("'name' is missing or empty");

        if (string.IsNullOrWhiteSpace(pack.Currencies.Cash.Label))
            errors.Add("'currencies.cash.label' is missing or empty");

        if (string.IsNullOrWhiteSpace(pack.Currencies.Premium.Label))
            errors.Add("'currencies.premium.label' is missing or empty");

        if (errors.Count > 0)
            throw new ContentPackException(
                $"Content pack '{expectedId}' failed validation: {string.Join("; ", errors)}.");
    }

    private static void ValidateJobs(JobCatalogFile file, string expectedPackId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(file.PackId))
            errors.Add("'packId' is missing or empty");
        else if (!string.Equals(file.PackId, expectedPackId, StringComparison.Ordinal))
            errors.Add($"'packId' mismatch: expected '{expectedPackId}', found '{file.PackId}'");

        // Mastery thresholds are pack-wide. maxRank 0 is legal and disables mastery entirely;
        // completionsPerRank is a divisor and can never be 0.
        if (file.Mastery.CompletionsPerRank < 1)
            errors.Add($"'mastery.completionsPerRank' must be >= 1, found {file.Mastery.CompletionsPerRank}");
        if (file.Mastery.MaxRank < 0)
            errors.Add($"'mastery.maxRank' must be >= 0, found {file.Mastery.MaxRank}");

        if (file.Jobs.Count == 0)
            errors.Add("'jobs' is missing or empty — a pack must define at least one job");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < file.Jobs.Count; i++)
        {
            var job = file.Jobs[i];

            // Identify the entry by id where possible, falling back to its index — an error about
            // "the job with no id" is useless without knowing which array slot it sits in.
            var where = string.IsNullOrWhiteSpace(job.Id) ? $"jobs[{i}]" : $"jobs[{i}] ('{job.Id}')";

            if (string.IsNullOrWhiteSpace(job.Id))
                errors.Add($"{where}: 'id' is missing or empty");
            else if (!seenIds.Add(job.Id))
                errors.Add($"{where}: duplicate 'id'");

            if (string.IsNullOrWhiteSpace(job.Name))
                errors.Add($"{where}: 'name' is missing or empty");

            if (job.EnergyCost < 1)
                errors.Add($"{where}: 'energyCost' must be >= 1, found {job.EnergyCost}");

            if (job.LevelRequirement < 1)
                errors.Add($"{where}: 'levelRequirement' must be >= 1, found {job.LevelRequirement}");

            if (job.Payout.CashMin < 0)
                errors.Add($"{where}: 'payout.cashMin' must be >= 0, found {job.Payout.CashMin}");

            if (job.Payout.CashMax < job.Payout.CashMin)
                errors.Add(
                    $"{where}: 'payout.cashMax' ({job.Payout.CashMax}) is below 'payout.cashMin' ({job.Payout.CashMin})");

            if (job.Payout.Xp < 0)
                errors.Add($"{where}: 'payout.xp' must be >= 0, found {job.Payout.Xp}");
        }

        if (errors.Count > 0)
            throw new ContentPackException(
                $"Job catalog for pack '{expectedPackId}' failed validation: {string.Join("; ", errors)}.");
    }
}
