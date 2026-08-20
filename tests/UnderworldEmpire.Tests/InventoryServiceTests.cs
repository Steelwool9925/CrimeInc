using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="InventoryService"/> (T3.1) — the rules of buying, equipping and
/// unequipping. Pure domain: no host, no database. Mirrors <see cref="JobServiceTests"/>, including its
/// central claim that a refusal mutates nothing.
/// </summary>
public class InventoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static Player TestPlayer(int level = 1, long cash = 100, int attack = 5, int defense = 5) => new()
    {
        CreatedAtUtc = Now,
        Level = level,
        Cash = cash,
        Attack = attack,
        Defense = defense
    };

    private static Item TestItem(
        string id = "brass-knuckles",
        ItemSlot slot = ItemSlot.Weapon,
        int levelRequirement = 1,
        long price = 75,
        int attack = 4,
        int defense = 0) => new()
        {
            Id = id,
            Name = id,
            Slot = slot,
            LevelRequirement = levelRequirement,
            Price = price,
            Attack = attack,
            Defense = defense
        };

    private static PlayerItem Holding(Guid playerId, string itemId, int copies = 0, bool equipped = false)
    {
        var row = new PlayerItem { PlayerId = playerId, ItemId = itemId };
        if (copies > 0)
            row.AddCopies(copies);
        if (equipped)
            row.Equip();
        return row;
    }

    // -------------------------------------------------------------------------
    // Buying
    // -------------------------------------------------------------------------

    [Fact]
    public void Buying_an_item_spends_the_price_and_adds_a_copy()
    {
        var player = TestPlayer(cash: 100);
        var item = TestItem(price: 75);
        var holding = Holding(player.Id, item.Id);

        var result = new InventoryService().Purchase(player, item, holding);

        Assert.True(result.Succeeded);
        Assert.Equal(75, result.CashSpent);
        Assert.Equal(1, result.QuantityOwned);
        Assert.Equal(25, player.Cash);
        Assert.Equal(1, holding.Quantity);
    }

    [Fact]
    public void Buying_a_second_copy_stacks_on_the_existing_holding()
    {
        var player = TestPlayer(cash: 200);
        var item = TestItem(price: 75);
        var holding = Holding(player.Id, item.Id, copies: 1);

        var result = new InventoryService().Purchase(player, item, holding);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.QuantityOwned);
        Assert.Equal(2, holding.Quantity);
        Assert.Equal(125, player.Cash);
    }

    [Fact]
    public void Cash_exactly_equal_to_the_price_is_enough()
    {
        var player = TestPlayer(cash: 75);
        var item = TestItem(price: 75);

        var result = new InventoryService().Purchase(player, item, Holding(player.Id, item.Id));

        Assert.True(result.Succeeded);
        Assert.Equal(0, player.Cash);
    }

    [Fact]
    public void Buying_below_the_level_requirement_is_refused_and_mutates_nothing()
    {
        var player = TestPlayer(level: 1, cash: 5000);
        var item = TestItem(levelRequirement: 5, price: 75);
        var holding = Holding(player.Id, item.Id);

        var result = new InventoryService().Purchase(player, item, holding);

        Assert.False(result.Succeeded);
        Assert.Equal(InventoryFailure.LevelTooLow, result.Failure);
        Assert.Equal(5000, player.Cash);
        Assert.Equal(0, holding.Quantity);
    }

    [Fact]
    public void Buying_without_enough_cash_is_refused_and_mutates_nothing()
    {
        var player = TestPlayer(cash: 74);
        var item = TestItem(price: 75);
        var holding = Holding(player.Id, item.Id);

        var result = new InventoryService().Purchase(player, item, holding);

        Assert.False(result.Succeeded);
        Assert.Equal(InventoryFailure.InsufficientCash, result.Failure);
        Assert.Equal(74, player.Cash);
        Assert.Equal(0, holding.Quantity);
    }

    [Fact]
    public void Level_is_reported_ahead_of_cash_when_both_would_refuse()
    {
        // Level is the permanent reason and cash the temporary one, so the player is told the thing
        // that will not fix itself. Same precedence as a job refusal.
        var player = TestPlayer(level: 1, cash: 0);
        var item = TestItem(levelRequirement: 9, price: 75);

        var result = new InventoryService().Purchase(player, item, Holding(player.Id, item.Id));

        Assert.Equal(InventoryFailure.LevelTooLow, result.Failure);
    }

    // -------------------------------------------------------------------------
    // Equipping
    // -------------------------------------------------------------------------

    [Fact]
    public void Equipping_an_owned_item_wears_it()
    {
        var player = TestPlayer();
        var item = TestItem();
        var holding = Holding(player.Id, item.Id, copies: 1);

        var result = new InventoryService().Equip(player, item, holding, []);

        Assert.True(result.Succeeded);
        Assert.Equal(item.Id, result.EquippedItemId);
        Assert.Null(result.UnequippedItemId);
        Assert.True(holding.Equipped);
    }

    [Fact]
    public void Equipping_replaces_whatever_was_already_worn_in_that_slot()
    {
        var player = TestPlayer();
        var knuckles = TestItem("brass-knuckles", ItemSlot.Weapon, attack: 4);
        var blade = TestItem("switchblade", ItemSlot.Weapon, attack: 7);
        var incumbent = Holding(player.Id, knuckles.Id, copies: 1, equipped: true);
        var wanted = Holding(player.Id, blade.Id, copies: 1);

        var result = new InventoryService().Equip(
            player, blade, wanted, [new OwnedItem(incumbent, knuckles)]);

        Assert.True(result.Succeeded);
        Assert.Equal("switchblade", result.EquippedItemId);
        Assert.Equal("brass-knuckles", result.UnequippedItemId);
        Assert.True(wanted.Equipped);
        Assert.False(incumbent.Equipped);
    }

    [Fact]
    public void Equipping_leaves_the_other_slots_alone()
    {
        var player = TestPlayer();
        var vest = TestItem("kevlar-vest", ItemSlot.Armor, defense: 14);
        var knuckles = TestItem("brass-knuckles", ItemSlot.Weapon, attack: 4);
        var vestHolding = Holding(player.Id, vest.Id, copies: 1, equipped: true);
        var wanted = Holding(player.Id, knuckles.Id, copies: 1);

        var result = new InventoryService().Equip(
            player, knuckles, wanted, [new OwnedItem(vestHolding, vest)]);

        Assert.True(result.Succeeded);
        Assert.Null(result.UnequippedItemId);
        Assert.True(vestHolding.Equipped);
        Assert.True(wanted.Equipped);
    }

    [Fact]
    public void An_owned_but_unworn_item_in_the_same_slot_is_not_reported_as_displaced()
    {
        // A caller that hands over every holding rather than only the equipped ones must not make the
        // service claim it took off something the player was never wearing — the client shows that name
        // in the result panel.
        var player = TestPlayer();
        var knuckles = TestItem("brass-knuckles", ItemSlot.Weapon, attack: 4);
        var blade = TestItem("switchblade", ItemSlot.Weapon, attack: 7);
        var ownedNotWorn = Holding(player.Id, knuckles.Id, copies: 1);
        var wanted = Holding(player.Id, blade.Id, copies: 1);

        var result = new InventoryService().Equip(
            player, blade, wanted, [new OwnedItem(ownedNotWorn, knuckles)]);

        Assert.True(result.Succeeded);
        Assert.Null(result.UnequippedItemId);
        Assert.True(wanted.Equipped);
    }

    [Fact]
    public void Equipping_an_item_the_player_does_not_own_is_refused()
    {
        var player = TestPlayer();
        var item = TestItem();
        var holding = Holding(player.Id, item.Id);

        var result = new InventoryService().Equip(player, item, holding, []);

        Assert.False(result.Succeeded);
        Assert.Equal(InventoryFailure.NotOwned, result.Failure);
        Assert.False(holding.Equipped);
    }

    [Fact]
    public void Equipping_above_the_players_level_is_refused()
    {
        var player = TestPlayer(level: 2);
        var item = TestItem(levelRequirement: 10);
        var holding = Holding(player.Id, item.Id, copies: 1);

        var result = new InventoryService().Equip(player, item, holding, []);

        Assert.False(result.Succeeded);
        Assert.Equal(InventoryFailure.LevelTooLow, result.Failure);
        Assert.False(holding.Equipped);
    }

    [Fact]
    public void Equipping_something_already_worn_is_refused_and_does_not_unequip_it()
    {
        var player = TestPlayer();
        var item = TestItem();
        var holding = Holding(player.Id, item.Id, copies: 1, equipped: true);

        var result = new InventoryService().Equip(
            player, item, holding, [new OwnedItem(holding, item)]);

        Assert.False(result.Succeeded);
        Assert.Equal(InventoryFailure.AlreadyEquipped, result.Failure);
        Assert.True(holding.Equipped);
    }

    // -------------------------------------------------------------------------
    // Unequipping
    // -------------------------------------------------------------------------

    [Fact]
    public void Unequipping_a_worn_item_takes_it_off_and_keeps_the_copies()
    {
        var player = TestPlayer();
        var item = TestItem();
        var holding = Holding(player.Id, item.Id, copies: 2, equipped: true);

        var result = new InventoryService().Unequip(holding);

        Assert.True(result.Succeeded);
        Assert.Equal(item.Id, result.UnequippedItemId);
        Assert.Null(result.EquippedItemId);
        Assert.False(holding.Equipped);
        Assert.Equal(2, holding.Quantity);
    }

    [Fact]
    public void Unequipping_something_that_is_not_worn_is_refused()
    {
        var player = TestPlayer();
        var holding = Holding(player.Id, "brass-knuckles", copies: 1);

        var result = new InventoryService().Unequip(holding);

        Assert.False(result.Succeeded);
        Assert.Equal(InventoryFailure.NotEquipped, result.Failure);
    }

    // -------------------------------------------------------------------------
    // The pay-off: what the player sees after gearing up
    // -------------------------------------------------------------------------

    [Fact]
    public void Buying_then_equipping_raises_the_effective_stats_which_is_what_CP3_checks()
    {
        var service = new InventoryService();
        var player = TestPlayer(cash: 100, attack: 5, defense: 5);
        var item = TestItem(price: 75, attack: 4, defense: 1);
        var holding = Holding(player.Id, item.Id);

        Assert.True(service.Purchase(player, item, holding).Succeeded);
        Assert.True(service.Equip(player, item, holding, []).Succeeded);

        var loadout = Loadout.For(player, [item]);
        Assert.Equal(9, loadout.TotalAttack);
        Assert.Equal(6, loadout.TotalDefense);
    }

    // -------------------------------------------------------------------------
    // Read-only gating — the same rules, asked without acting on them
    // -------------------------------------------------------------------------
    //
    // GET /inventory has to tell the client which buttons are live. Re-deriving that in the endpoint
    // would be a second copy of the precedence below, free to drift from the one that actually
    // refuses the request — the client would offer a button the server then rejects.

    [Fact]
    public void An_affordable_in_level_item_reports_no_purchase_obstacle()
    {
        var service = new InventoryService();

        Assert.Equal(
            InventoryFailure.None,
            service.CheckPurchase(TestPlayer(level: 1, cash: 100), TestItem(levelRequirement: 1, price: 75)));
    }

    [Fact]
    public void Checking_a_purchase_reports_level_ahead_of_cash_exactly_as_purchasing_does()
    {
        var service = new InventoryService();
        var player = TestPlayer(level: 1, cash: 0);
        var item = TestItem(levelRequirement: 5, price: 600);

        // Both obstacles are present. The level is the permanent one, so it is the one reported.
        Assert.Equal(InventoryFailure.LevelTooLow, service.CheckPurchase(player, item));
        Assert.Equal(InventoryFailure.LevelTooLow, service.Purchase(player, item, Holding(player.Id, item.Id)).Failure);
    }

    [Fact]
    public void Checking_a_purchase_reports_cash_when_that_is_the_only_obstacle()
    {
        var service = new InventoryService();

        Assert.Equal(
            InventoryFailure.InsufficientCash,
            service.CheckPurchase(TestPlayer(level: 1, cash: 74), TestItem(levelRequirement: 1, price: 75)));
    }

    [Fact]
    public void Checking_a_purchase_does_not_charge_the_player()
    {
        var service = new InventoryService();
        var player = TestPlayer(cash: 100);

        service.CheckPurchase(player, TestItem(price: 75));

        Assert.Equal(100, player.Cash);
    }

    [Fact]
    public void An_owned_in_level_unworn_item_reports_no_equip_obstacle()
    {
        var service = new InventoryService();
        var player = TestPlayer(level: 1);
        var item = TestItem(levelRequirement: 1);

        Assert.Equal(
            InventoryFailure.None,
            service.CheckEquip(player, item, Holding(player.Id, item.Id, copies: 1)));
    }

    [Fact]
    public void Checking_an_equip_reports_not_owned_before_anything_else()
    {
        var service = new InventoryService();
        var player = TestPlayer(level: 1);
        var item = TestItem(levelRequirement: 5);

        // Out-levelled and unowned at once: not owning it is the obstacle worth naming, because a
        // player who does not have the item has nothing to act on.
        Assert.Equal(
            InventoryFailure.NotOwned,
            service.CheckEquip(player, item, Holding(player.Id, item.Id)));
    }

    [Fact]
    public void Checking_an_equip_reports_the_level_gate_on_gear_bought_before_a_level_reset()
    {
        var service = new InventoryService();
        var player = TestPlayer(level: 1);
        var item = TestItem(levelRequirement: 5);

        Assert.Equal(
            InventoryFailure.LevelTooLow,
            service.CheckEquip(player, item, Holding(player.Id, item.Id, copies: 1)));
    }

    [Fact]
    public void Checking_an_equip_reports_gear_already_being_worn()
    {
        var service = new InventoryService();
        var player = TestPlayer(level: 1);
        var item = TestItem(levelRequirement: 1);

        Assert.Equal(
            InventoryFailure.AlreadyEquipped,
            service.CheckEquip(player, item, Holding(player.Id, item.Id, copies: 1, equipped: true)));
    }

    [Fact]
    public void Checking_an_equip_does_not_equip_anything()
    {
        var service = new InventoryService();
        var player = TestPlayer(level: 1);
        var item = TestItem(levelRequirement: 1);
        var holding = Holding(player.Id, item.Id, copies: 1);

        service.CheckEquip(player, item, holding);

        Assert.False(holding.Equipped);
    }
}
