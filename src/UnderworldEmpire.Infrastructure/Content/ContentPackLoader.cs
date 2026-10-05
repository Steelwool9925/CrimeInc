using System.Text;
using System.Text.Json;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Domain.Properties;

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
    /// The folder holding <paramref name="packId"/>'s files. Asset serving (T4.1) resolves
    /// manifest-declared relative paths against this, and confines them to it.
    /// </summary>
    public string PackRoot(string packId) => Path.Combine(_contentRoot, packId);

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
    /// <param name="packId">The pack to load.</param>
    /// <param name="itemCatalog">
    ///     The pack's already-loaded item catalog, used to validate every job's
    ///     <c>itemRequirements</c> reference a real weapon or armor item — items and jobs are
    ///     otherwise independent catalogs, so this is the one place they cross. Omitted (or
    ///     <see langword="null"/>), that cross-catalog check is skipped entirely, which is the right
    ///     answer for a caller with no items to validate against (every test fixture that does not
    ///     exercise item requirements). The real startup path (<c>AddInfrastructure</c>) always
    ///     supplies one, loading items first for exactly this reason.
    /// </param>
    /// <exception cref="ContentPackException">
    ///     Thrown when the file is missing, cannot be parsed, or fails validation.
    /// </exception>
    public JobCatalog LoadJobs(string packId, ItemCatalog? itemCatalog = null)
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

        ValidateJobs(file, packId, itemCatalog);

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
                Xp = entry.Payout.Xp,
                // ValidateJobs has already proved both blocks are present, so the null-forgiving
                // access asserts that rather than hoping — the same posture ParseSlot takes in
                // LoadItems.
                AttackRequirement = entry.Requirements!.Attack,
                DefenseRequirement = entry.Requirements.Defense,
                BaseSuccessChance = entry.Risk!.BaseSuccessChance,
                SuccessChancePerSurplusPoint = entry.Risk.SuccessChancePerSurplusPoint,
                ItemRequirements = (entry.ItemRequirements ?? [])
                    .Select(r => new ItemRequirement(r.ItemId, r.MinQuantity, r.QuantityConsumed))
                    .ToList(),
                ItemSuccessChancePerSurplusPoint = entry.Risk.ItemSuccessChancePerSurplusPoint,
                HealthLossMin = entry.Risk.HealthLossMin,
                HealthLossMax = entry.Risk.HealthLossMax
            };
            job.EnsureValid();
            jobs.Add(job);
        }

        return new JobCatalog(
            jobs,
            file.Mastery.CompletionsPerRank,
            file.Mastery.MaxRank,
            file.Risk!.MaxSuccessChance,
            file.Risk.ConsolationCashFraction,
            file.Risk.ConsolationXpFraction);
    }

    /// <summary>
    /// Loads and validates the item catalog for <paramref name="packId"/> from
    /// <c>&lt;contentRoot&gt;/&lt;packId&gt;/items.json</c>.
    /// </summary>
    /// <remarks>
    /// Same contract as <see cref="LoadJobs"/>: validation runs to completion and reports every problem at
    /// once, and domain objects are constructed only after it passes.
    /// </remarks>
    /// <exception cref="ContentPackException">
    ///     Thrown when the file is missing, cannot be parsed, or fails validation.
    /// </exception>
    public ItemCatalog LoadItems(string packId)
    {
        var itemsFile = Path.Combine(_contentRoot, packId, "items.json");

        if (!File.Exists(itemsFile))
            throw new ContentPackException(
                $"Item catalog for pack '{packId}' not found at '{itemsFile}'.");

        ItemCatalogFile file;
        try
        {
            var json = File.ReadAllText(itemsFile, Encoding.UTF8);
            file = JsonSerializer.Deserialize<ItemCatalogFile>(json, JsonOptions)
                   ?? throw new ContentPackException(
                       $"items.json for '{packId}' deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new ContentPackException(
                $"items.json for '{packId}' is not valid JSON: {ex.Message}", ex);
        }

        ValidateItems(file, packId);

        var items = new List<Item>(file.Items.Count);
        foreach (var entry in file.Items)
        {
            var item = new Item
            {
                Id = entry.Id,
                Name = entry.Name,
                Description = entry.Description,
                // ValidateItems has already proved every slot name parses, so the null-forgiving access is
                // asserting that rather than hoping — a second error path here could never fire.
                Slot = ParseSlot(entry.Slot)!.Value,
                LevelRequirement = entry.LevelRequirement,
                Price = entry.Price,
                Attack = entry.Stats.Attack,
                Defense = entry.Stats.Defense
            };
            item.EnsureValid();
            items.Add(item);
        }

        return new ItemCatalog(items);
    }


    /// <summary>
    /// Loads and validates the property catalog for <paramref name="packId"/> from
    /// <c>&lt;contentRoot&gt;/&lt;packId&gt;/properties.json</c>.
    /// </summary>
    /// <remarks>
    /// Same contract as <see cref="LoadItems"/>: validation runs to completion and reports every problem
    /// at once, and domain objects are constructed only after it passes.
    /// </remarks>
    /// <exception cref="ContentPackException">
    ///     Thrown when the file is missing, cannot be parsed, or fails validation.
    /// </exception>
    public PropertyCatalog LoadProperties(string packId)
    {
        var propertiesFile = Path.Combine(_contentRoot, packId, "properties.json");

        if (!File.Exists(propertiesFile))
            throw new ContentPackException(
                $"Property catalog for pack '{packId}' not found at '{propertiesFile}'.");

        PropertyCatalogFile file;
        try
        {
            var json = File.ReadAllText(propertiesFile, Encoding.UTF8);
            file = JsonSerializer.Deserialize<PropertyCatalogFile>(json, JsonOptions)
                   ?? throw new ContentPackException(
                       $"properties.json for '{packId}' deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new ContentPackException(
                $"properties.json for '{packId}' is not valid JSON: {ex.Message}", ex);
        }

        ValidateProperties(file, packId);

        var properties = new List<Property>(file.Properties.Count);
        foreach (var entry in file.Properties)
        {
            var property = new Property
            {
                Id = entry.Id,
                Name = entry.Name,
                Description = entry.Description,
                LevelRequirement = entry.LevelRequirement,
                Price = entry.Price,
                IncomePerHour = entry.Income.PerHour,
                AccrualCapMinutes = entry.Income.CapMinutes
            };
            property.EnsureValid();
            properties.Add(property);
        }

        return new PropertyCatalog(properties);
    }

    /// <summary>The slot names content may use, for error messages.</summary>
    private static readonly string SlotNames = string.Join(", ",
        Enum.GetNames<ItemSlot>().Select(n => n.ToLowerInvariant()));

    /// <summary>
    /// Parses a content slot name, or <see langword="null"/> when it names no known
    /// <see cref="ItemSlot"/>. Case-insensitive: the slot vocabulary is the engine's, not a content key,
    /// so casing in the pack file is a style choice.
    /// </summary>
    /// <remarks>
    /// An explicit match rather than <c>Enum.TryParse</c>, which also accepts the underlying numbers —
    /// <c>"slot": "0"</c> would parse as <see cref="ItemSlot.Weapon"/> and let a typo silently mis-slot an
    /// item. Content names slots by name only.
    /// </remarks>
    private static ItemSlot? ParseSlot(string? slot) => slot?.Trim().ToLowerInvariant() switch
    {
        "weapon" => ItemSlot.Weapon,
        "armor" => ItemSlot.Armor,
        "vehicle" => ItemSlot.Vehicle,
        _ => null
    };

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

    private static void ValidateJobs(JobCatalogFile file, string expectedPackId, ItemCatalog? itemCatalog)
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

        // Risk rules are pack-wide and required. A missing block is reported rather than defaulted:
        // zeros would read as a ceiling of zero, making every job in the pack impossible.
        if (file.Risk is null)
        {
            errors.Add("'risk' is missing — a pack must define its risk rules");
        }
        else
        {
            // Zero is rejected alongside negatives: a ceiling of zero has made every job impossible,
            // which is never what was meant by writing the block at all.
            if (file.Risk.MaxSuccessChance is <= 0.0 or > 1.0)
                errors.Add($"'risk.maxSuccessChance' must be > 0 and <= 1, found {file.Risk.MaxSuccessChance}");
            if (file.Risk.ConsolationCashFraction is < 0.0 or > 1.0)
                errors.Add($"'risk.consolationCashFraction' must be between 0 and 1, found {file.Risk.ConsolationCashFraction}");
            if (file.Risk.ConsolationXpFraction is < 0.0 or > 1.0)
                errors.Add($"'risk.consolationXpFraction' must be between 0 and 1, found {file.Risk.ConsolationXpFraction}");
        }

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

            // Absent, this would read as "ungated" — which is exactly what an author who forgot the
            // block would fail to notice, since the job would simply work.
            if (job.Requirements is null)
            {
                errors.Add($"{where}: 'requirements' is missing");
            }
            else
            {
                if (job.Requirements.Attack < 0)
                    errors.Add($"{where}: 'requirements.attack' must be >= 0, found {job.Requirements.Attack}");
                if (job.Requirements.Defense < 0)
                    errors.Add($"{where}: 'requirements.defense' must be >= 0, found {job.Requirements.Defense}");
            }

            if (job.Risk is null)
            {
                errors.Add($"{where}: 'risk' is missing");
            }
            else
            {
                if (job.Risk.BaseSuccessChance is < 0.0 or > 1.0)
                    errors.Add($"{where}: 'risk.baseSuccessChance' must be between 0 and 1, found {job.Risk.BaseSuccessChance}");

                // Job.SuccessChanceFor throws on this contradiction at roll time. Catching it here
                // turns a request-time crash into a startup message naming the job.
                else if (file.Risk is not null && job.Risk.BaseSuccessChance > file.Risk.MaxSuccessChance)
                    errors.Add(
                        $"{where}: 'risk.baseSuccessChance' ({job.Risk.BaseSuccessChance}) is above " +
                        $"'risk.maxSuccessChance' ({file.Risk.MaxSuccessChance})");

                if (job.Risk.SuccessChancePerSurplusPoint < 0.0)
                    errors.Add($"{where}: 'risk.successChancePerSurplusPoint' must be >= 0, found {job.Risk.SuccessChancePerSurplusPoint}");

                if (job.Risk.HealthLossMin < 0)
                    errors.Add($"{where}: 'risk.healthLossMin' must be >= 0, found {job.Risk.HealthLossMin}");

                if (job.Risk.HealthLossMax < job.Risk.HealthLossMin)
                    errors.Add(
                        $"{where}: 'risk.healthLossMax' ({job.Risk.HealthLossMax}) is below " +
                        $"'risk.healthLossMin' ({job.Risk.HealthLossMin})");

                if (job.Risk.ItemSuccessChancePerSurplusPoint < 0.0)
                    errors.Add(
                        $"{where}: 'risk.itemSuccessChancePerSurplusPoint' must be >= 0, found " +
                        $"{job.Risk.ItemSuccessChancePerSurplusPoint}");
            }

            // Absent means ungated — unlike 'requirements' and 'risk' above, every job that predates
            // this field must keep working without it, so omission is the ordinary case here.
            if (job.ItemRequirements is not null)
            {
                var seenItemIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var requirement in job.ItemRequirements)
                {
                    if (string.IsNullOrWhiteSpace(requirement.ItemId))
                    {
                        errors.Add($"{where}: 'itemRequirements' has an entry with a missing or empty 'itemId'");
                        continue;
                    }

                    if (!seenItemIds.Add(requirement.ItemId))
                        errors.Add($"{where}: 'itemRequirements' has a duplicate entry for '{requirement.ItemId}'");

                    if (requirement.MinQuantity < 1)
                        errors.Add(
                            $"{where}: 'itemRequirements' entry for '{requirement.ItemId}' has " +
                            $"'minQuantity' {requirement.MinQuantity}, must be >= 1");

                    // The gate only guarantees MinQuantity copies, so spending more than that
                    // would overdraw the stack on the one attempt that was just admitted.
                    // Skipped when minQuantity is itself invalid: that is already reported above, and
                    // the range would only echo it as a second, derived error.
                    if (requirement.MinQuantity >= 1 &&
                        (requirement.QuantityConsumed < 1 ||
                         requirement.QuantityConsumed > requirement.MinQuantity))
                        errors.Add(
                            $"{where}: 'itemRequirements' entry for '{requirement.ItemId}' has " +
                            $"'quantityConsumed' {requirement.QuantityConsumed}, must be between 1 and " +
                            $"'minQuantity' ({requirement.MinQuantity})");

                    // The one place items and jobs cross: jobs.json cannot reference an item.json id
                    // that does not exist, or that exists but is not equippable gear. Skipped when no
                    // item catalog was supplied at all (see LoadJobs's itemCatalog remarks) rather
                    // than when the id simply fails to resolve — those are different situations and
                    // only the latter is a content mistake.
                    else if (itemCatalog is not null)
                    {
                        var referenced = itemCatalog.Find(requirement.ItemId);
                        if (referenced is null)
                            errors.Add(
                                $"{where}: 'itemRequirements' references unknown item id " +
                                $"'{requirement.ItemId}'");
                        else if (referenced.Slot == ItemSlot.Vehicle)
                            errors.Add(
                                $"{where}: 'itemRequirements' references '{requirement.ItemId}', which " +
                                "is a vehicle — only weapon or armor items can gate a job");
                    }
                }
            }
        }

        if (errors.Count > 0)
            throw new ContentPackException(
                $"Job catalog for pack '{expectedPackId}' failed validation: {string.Join("; ", errors)}.");
    }

    /// <summary>
    /// Maximum length of a content id that is used as a database key. Must stay in step with the
    /// <c>varchar</c> width <c>GameDbContext</c> maps for <c>PlayerItem.ItemId</c>.
    /// </summary>
    /// <remarks>
    /// Validated at startup rather than left to Postgres, because the failure would otherwise land as an
    /// unhandled write error on the first purchase of an over-long id — long after the content mistake was
    /// made, and as a 500 rather than a startup message.
    /// </remarks>
    private const int ContentIdMaxLength = 64;

    private static void ValidateItems(ItemCatalogFile file, string expectedPackId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(file.PackId))
            errors.Add("'packId' is missing or empty");
        else if (!string.Equals(file.PackId, expectedPackId, StringComparison.Ordinal))
            errors.Add($"'packId' mismatch: expected '{expectedPackId}', found '{file.PackId}'");

        if (file.Items.Count == 0)
            errors.Add("'items' is missing or empty — a pack must define at least one item");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < file.Items.Count; i++)
        {
            var item = file.Items[i];

            // Identify the entry by id where possible, falling back to its index — an error about "the
            // item with no id" is useless without knowing which array slot it sits in.
            var where = string.IsNullOrWhiteSpace(item.Id) ? $"items[{i}]" : $"items[{i}] ('{item.Id}')";

            if (string.IsNullOrWhiteSpace(item.Id))
                errors.Add($"{where}: 'id' is missing or empty");
            else
            {
                if (!seenIds.Add(item.Id))
                    errors.Add($"{where}: duplicate 'id'");

                if (item.Id.Length > ContentIdMaxLength)
                    errors.Add(
                        $"{where}: 'id' is {item.Id.Length} characters, above the {ContentIdMaxLength}-character limit");
            }

            if (string.IsNullOrWhiteSpace(item.Name))
                errors.Add($"{where}: 'name' is missing or empty");

            if (ParseSlot(item.Slot) is null)
                errors.Add(
                    $"{where}: 'slot' is '{item.Slot}', which is not one of: {SlotNames}");

            if (item.LevelRequirement < 1)
                errors.Add($"{where}: 'levelRequirement' must be >= 1, found {item.LevelRequirement}");

            if (item.Price < 0)
                errors.Add($"{where}: 'price' must be >= 0, found {item.Price}");

            if (item.Stats.Attack < 0)
                errors.Add($"{where}: 'stats.attack' must be >= 0, found {item.Stats.Attack}");

            if (item.Stats.Defense < 0)
                errors.Add($"{where}: 'stats.defense' must be >= 0, found {item.Stats.Defense}");
        }

        if (errors.Count > 0)
            throw new ContentPackException(
                $"Item catalog for pack '{expectedPackId}' failed validation: {string.Join("; ", errors)}.");
    }

    private static void ValidateProperties(PropertyCatalogFile file, string expectedPackId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(file.PackId))
            errors.Add("'packId' is missing or empty");
        else if (!string.Equals(file.PackId, expectedPackId, StringComparison.Ordinal))
            errors.Add($"'packId' mismatch: expected '{expectedPackId}', found '{file.PackId}'");

        if (file.Properties.Count == 0)
            errors.Add("'properties' is missing or empty — a pack must define at least one property");

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < file.Properties.Count; i++)
        {
            var property = file.Properties[i];

            // Identify the entry by id where possible, falling back to its index — the same convention
            // ValidateItems uses, so a content author reads one error format across every catalog.
            var where = string.IsNullOrWhiteSpace(property.Id)
                ? $"properties[{i}]"
                : $"properties[{i}] ('{property.Id}')";

            if (string.IsNullOrWhiteSpace(property.Id))
                errors.Add($"{where}: 'id' is missing or empty");
            else
            {
                if (!seenIds.Add(property.Id))
                    errors.Add($"{where}: duplicate 'id'");

                if (property.Id.Length > ContentIdMaxLength)
                    errors.Add(
                        $"{where}: 'id' is {property.Id.Length} characters, above the {ContentIdMaxLength}-character limit");
            }

            if (string.IsNullOrWhiteSpace(property.Name))
                errors.Add($"{where}: 'name' is missing or empty");

            if (property.LevelRequirement < 1)
                errors.Add($"{where}: 'levelRequirement' must be >= 1, found {property.LevelRequirement}");

            if (property.Price < 0)
                errors.Add($"{where}: 'price' must be >= 0, found {property.Price}");

            if (property.Income.PerHour < 1)
                errors.Add($"{where}: 'income.perHour' must be >= 1, found {property.Income.PerHour}");

            else if (property.Income.PerHour > Property.MaxIncomePerHour)
                errors.Add(
                    $"{where}: 'income.perHour' must be <= {Property.MaxIncomePerHour}, found {property.Income.PerHour}");

            if (property.Income.CapMinutes < 1)
                errors.Add($"{where}: 'income.capMinutes' must be >= 1, found {property.Income.CapMinutes}");
            else if (property.Income.CapMinutes > Property.MaxAccrualCapMinutes)
                errors.Add(
                    $"{where}: 'income.capMinutes' must be <= {Property.MaxAccrualCapMinutes} (30 days), " +
                    $"found {property.Income.CapMinutes}");

            // Both fields can be individually legal and still multiply out to nothing once floored to
            // whole cash — 30/hour over one minute is half a coin. Only the product catches it, and
            // catching it here beats shipping a property that charges for a purchase and never pays.
            if (property.Income.PerHour >= 1
                && property.Income.PerHour <= Property.MaxIncomePerHour
                && property.Income.CapMinutes >= 1
                && property.Income.CapMinutes <= Property.MaxAccrualCapMinutes
                && property.Income.PerHour * property.Income.CapMinutes / 60L < 1)
            {
                errors.Add(
                    $"{where}: 'income' of {property.Income.PerHour}/hour over {property.Income.CapMinutes} " +
                    "minutes floors to zero cash — the property could never pay out");
            }
        }

        if (errors.Count > 0)
            throw new ContentPackException(
                $"Property catalog for pack '{expectedPackId}' failed validation: {string.Join("; ", errors)}.");
    }
}
