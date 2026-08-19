using UnderworldEmpire.Domain.Items;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="Item"/> (T3.1) — the content-supplied item definition and the
/// invariants the engine relies on. Mirrors the guard tests around <c>Job</c>: the content loader is
/// the primary validation path, but <see cref="Item.EnsureValid"/> protects hand-built items.
/// </summary>
public class ItemTests
{
    private static Item TestItem(
        string id = "brass-knuckles",
        string name = "Brass Knuckles",
        ItemSlot slot = ItemSlot.Weapon,
        int levelRequirement = 1,
        long price = 75,
        int attack = 4,
        int defense = 0) => new()
        {
            Id = id,
            Name = name,
            Slot = slot,
            LevelRequirement = levelRequirement,
            Price = price,
            Attack = attack,
            Defense = defense
        };

    [Fact]
    public void A_valid_item_passes_validation()
    {
        TestItem().EnsureValid();
    }

    [Fact]
    public void A_blank_id_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => TestItem(id: "  ").EnsureValid());
    }

    [Fact]
    public void A_blank_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => TestItem(name: "").EnsureValid());
    }

    [Fact]
    public void A_level_requirement_below_one_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestItem(levelRequirement: 0).EnsureValid());
    }

    [Fact]
    public void A_negative_price_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestItem(price: -1).EnsureValid());
    }

    [Fact]
    public void A_negative_attack_bonus_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestItem(attack: -1).EnsureValid());
    }

    [Fact]
    public void A_negative_defense_bonus_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestItem(defense: -1).EnsureValid());
    }

    [Fact]
    public void An_item_may_be_free_and_may_grant_no_bonus_at_all()
    {
        // A cosmetic or starter item is legal content; only negatives are rejected.
        TestItem(price: 0, attack: 0, defense: 0).EnsureValid();
    }
}
