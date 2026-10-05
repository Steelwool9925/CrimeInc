using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Pins the one economic property no single catalogue can see: <b>attempting a job must never cost
/// the player money in expectation.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class exists.</b> <c>hijack-a-delivery-truck</c> shipped paying a mean of 200 at a 0.65
/// chance, which is worth about 130.7 an attempt, while spending a 180 switchblade every time: roughly
/// −49 per attempt, so a player who ground it was strictly worse off than one who ignored it. Nothing
/// caught it because the two facts needed to see it live in different files — <c>jobs.json</c> knows
/// the job consumes a switchblade, <c>items.json</c> knows a switchblade costs 180 — and no test had
/// ever held both at once. This one loads both from the real pack and checks every job.
/// </para>
/// <para>
/// <b>It deliberately uses the base success chance</b>, the floor a player exactly on the requirements
/// gets. Surplus stats and surplus stock only raise the chance, so a job that is profitable at the
/// floor is profitable for everyone, and one that is not is a trap for the player who has only just
/// unlocked it.
/// </para>
/// <para>
/// <b>This encodes a design opinion, and a deliberate loss-leader will trip it.</b> A tutorial sink or a
/// story beat that costs the player money would be a legitimate thing to want. The way to do that is an
/// explicit, named exemption in content that this test reads — never deleting or loosening the
/// assertion, which would silently re-permit exactly the bug above. No exemption exists today because
/// no job needs one.
/// </para>
/// </remarks>
public class JobEconomyInvariantTests
{
    private static (JobCatalog Jobs, ItemCatalog Items) RealPack()
    {
        var loader = new ContentPackLoader(RepoContent.FindRoot());
        var items = loader.LoadItems("crime");
        return (loader.LoadJobs("crime", items), items);
    }

    /// <summary>
    /// What an attempt is worth on average at the base chance: the full payout on a success, and the
    /// consolation fraction of it on a failure (the cash range is rolled on both branches).
    /// </summary>
    private static double ExpectedCash(Job job, double consolationCashFraction)
    {
        double meanPayout = (job.CashMin + job.CashMax) / 2.0;
        return job.BaseSuccessChance * meanPayout
             + (1.0 - job.BaseSuccessChance) * meanPayout * consolationCashFraction;
    }

    /// <summary>The market price of everything one attempt spends, win or lose.</summary>
    private static long ConsumedItemCost(Job job, ItemCatalog items) =>
        job.ItemRequirements.Sum(requirement =>
        {
            var item = items.Find(requirement.ItemId)
                ?? throw new InvalidOperationException(
                    $"Job '{job.Id}' consumes '{requirement.ItemId}', which is not in the item catalogue.");
            return item.Price * requirement.QuantityConsumed;
        });

    [Fact]
    public void No_job_in_the_pack_costs_the_player_money_in_expectation()
    {
        var (catalog, items) = RealPack();

        var losers = catalog.Jobs
            .Select(job => (
                job.Id,
                Expected: ExpectedCash(job, catalog.ConsolationCashFraction),
                Cost: ConsumedItemCost(job, items)))
            .Where(j => j.Expected - j.Cost <= 0)
            .Select(j => $"'{j.Id}' is expected to earn {j.Expected:0.##} and consumes " +
                         $"{j.Cost} of items, a net of {j.Expected - j.Cost:0.##} per attempt")
            .ToList();

        Assert.True(losers.Count == 0,
            "A job that costs the player money every time they do it is a trap the UI never mentions. " +
            string.Join("; ", losers) + ". Raise the payout, lower the consumed item's price, or drop " +
            "the consumable requirement. If the loss is deliberate, add an explicit exemption to " +
            "content — do not loosen this test.");
    }

    [Fact]
    public void The_invariant_would_have_caught_the_original_hijack_numbers()
    {
        // The test above passing proves the pack is fine today; it says nothing about whether the
        // check can fail. This rebuilds the job as it shipped (140-260, a 180 blade, 0.65, a 1% consolation)
        // from literals and shows the same arithmetic rejects it, so the guard is not vacuous. It does
        // not read the real pack on purpose: retuning the blade's price, the job's odds or its id must
        // not break a test whose only job is to prove the guard can fail.
        var item = new Item
        {
            Id = "switchblade",
            Name = "Switchblade",
            Slot = ItemSlot.Weapon,
            LevelRequirement = 2,
            Price = 180,
        };
        var items = new ItemCatalog([item]);
        var shipped = new Job
        {
            Id = "hijack-a-delivery-truck",
            Name = "Hijack a Delivery Truck",
            EnergyCost = 4,
            LevelRequirement = 3,
            CashMin = 140,
            CashMax = 260,
            Xp = 55,
            BaseSuccessChance = 0.65,
            ItemRequirements = [new ItemRequirement("switchblade", MinQuantity: 2)],
        };

        double net = ExpectedCash(shipped, consolationCashFraction: 0.01)
                   - ConsumedItemCost(shipped, items);

        Assert.True(net < 0, $"Expected the original numbers to lose money, got a net of {net:0.##}.");
        Assert.Equal(-49.3, net, precision: 1);
    }

    [Fact]
    public void A_job_that_pays_for_what_it_consumes_passes_the_same_arithmetic()
    {
        // The other half of "the guard is not vacuous": it must also *accept* a sound job, or it would
        // pass the first test by rejecting everything. The same job with the payout the fix chose.
        var items = new ItemCatalog([new Item
        {
            Id = "switchblade",
            Name = "Switchblade",
            Slot = ItemSlot.Weapon,
            LevelRequirement = 2,
            Price = 180,
        }]);
        var fixedJob = new Job
        {
            Id = "hijack-a-delivery-truck",
            Name = "Hijack a Delivery Truck",
            EnergyCost = 4,
            LevelRequirement = 3,
            CashMin = 260,
            CashMax = 480,
            Xp = 55,
            BaseSuccessChance = 0.65,
            ItemRequirements = [new ItemRequirement("switchblade", MinQuantity: 1)],
        };

        double net = ExpectedCash(fixedJob, consolationCashFraction: 0.01)
                   - ConsumedItemCost(fixedJob, items);

        Assert.Equal(61.8, net, precision: 1);
    }
}
