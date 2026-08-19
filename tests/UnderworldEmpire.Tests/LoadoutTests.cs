using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="Loadout"/> (T3.1) — the attack/defense aggregation the plan calls out
/// for TDD, and the figure CP3 asks the tester to watch rise. Pure domain: no host, no database.
/// </summary>
public class LoadoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static Player TestPlayer(int attack = 5, int defense = 5, int level = 1) => new()
    {
        CreatedAtUtc = Now,
        Attack = attack,
        Defense = defense,
        Level = level
    };

    private static Item TestItem(
        string id,
        ItemSlot slot,
        int attack = 0,
        int defense = 0) => new()
        {
            Id = id,
            Name = id,
            Slot = slot,
            LevelRequirement = 1,
            Price = 100,
            Attack = attack,
            Defense = defense
        };

    // -------------------------------------------------------------------------
    // Aggregation
    // -------------------------------------------------------------------------

    [Fact]
    public void With_nothing_equipped_the_totals_are_the_players_base_stats()
    {
        var loadout = Loadout.For(TestPlayer(attack: 7, defense: 3), []);

        Assert.Equal(7, loadout.TotalAttack);
        Assert.Equal(3, loadout.TotalDefense);
        Assert.Equal(0, loadout.ItemAttack);
        Assert.Equal(0, loadout.ItemDefense);
    }

    [Fact]
    public void An_equipped_item_adds_its_bonuses_to_the_base_stats()
    {
        var knuckles = TestItem("brass-knuckles", ItemSlot.Weapon, attack: 4);

        var loadout = Loadout.For(TestPlayer(attack: 5, defense: 5), [knuckles]);

        Assert.Equal(9, loadout.TotalAttack);
        Assert.Equal(5, loadout.TotalDefense);
    }

    [Fact]
    public void Bonuses_from_every_slot_are_summed_together()
    {
        Item[] equipped =
        [
            TestItem("switchblade", ItemSlot.Weapon, attack: 7, defense: 1),
            TestItem("kevlar-vest", ItemSlot.Armor, attack: 1, defense: 14),
            TestItem("stolen-sedan", ItemSlot.Vehicle, attack: 6, defense: 8)
        ];

        var loadout = Loadout.For(TestPlayer(attack: 5, defense: 5), equipped);

        Assert.Equal(14, loadout.ItemAttack);
        Assert.Equal(23, loadout.ItemDefense);
        Assert.Equal(19, loadout.TotalAttack);
        Assert.Equal(28, loadout.TotalDefense);
    }

    [Fact]
    public void The_item_contribution_is_reported_apart_from_the_base_so_a_client_can_show_both()
    {
        var loadout = Loadout.For(
            TestPlayer(attack: 5, defense: 6),
            [TestItem("kevlar-vest", ItemSlot.Armor, defense: 14)]);

        Assert.Equal(5, loadout.BaseAttack);
        Assert.Equal(6, loadout.BaseDefense);
        Assert.Equal(0, loadout.ItemAttack);
        Assert.Equal(14, loadout.ItemDefense);
    }

    // -------------------------------------------------------------------------
    // Slot lookup
    // -------------------------------------------------------------------------

    [Fact]
    public void The_item_worn_in_a_slot_can_be_looked_up()
    {
        var vest = TestItem("kevlar-vest", ItemSlot.Armor, defense: 14);

        var loadout = Loadout.For(TestPlayer(), [vest]);

        Assert.Same(vest, loadout.InSlot(ItemSlot.Armor));
    }

    [Fact]
    public void An_empty_slot_reports_nothing_worn()
    {
        var loadout = Loadout.For(TestPlayer(), [TestItem("brass-knuckles", ItemSlot.Weapon, attack: 4)]);

        Assert.Null(loadout.InSlot(ItemSlot.Vehicle));
    }

    [Fact]
    public void Equipped_items_are_listed_in_the_order_they_were_supplied()
    {
        Item[] equipped =
        [
            TestItem("kevlar-vest", ItemSlot.Armor, defense: 14),
            TestItem("brass-knuckles", ItemSlot.Weapon, attack: 4)
        ];

        var loadout = Loadout.For(TestPlayer(), equipped);

        Assert.Equal(["kevlar-vest", "brass-knuckles"], loadout.Equipped.Select(i => i.Id));
    }

    // -------------------------------------------------------------------------
    // The one-per-slot invariant
    // -------------------------------------------------------------------------

    [Fact]
    public void Two_items_worn_in_the_same_slot_is_a_corrupt_loadout_and_is_refused()
    {
        // The rule is enforced on the write path by InventoryService.Equip and cannot be a database
        // constraint (slot is content, not a column), so aggregation refuses to silently double-count.
        Item[] equipped =
        [
            TestItem("brass-knuckles", ItemSlot.Weapon, attack: 4),
            TestItem("switchblade", ItemSlot.Weapon, attack: 7)
        ];

        var ex = Assert.Throws<InvalidOperationException>(() => Loadout.For(TestPlayer(), equipped));

        Assert.Contains("Weapon", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_player_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Loadout.For(null!, []));
    }

    [Fact]
    public void A_null_item_collection_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Loadout.For(TestPlayer(), null!));
    }
}
