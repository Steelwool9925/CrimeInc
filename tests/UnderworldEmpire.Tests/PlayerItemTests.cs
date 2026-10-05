using UnderworldEmpire.Domain.Items;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Unit tests for <see cref="PlayerItem"/> (T3.1) — one player's holding of one item. Pure domain:
/// no host, no database.
/// </summary>
public class PlayerItemTests
{
    private static PlayerItem NewRow(string itemId = "brass-knuckles") =>
        new() { PlayerId = Guid.NewGuid(), ItemId = itemId };

    private static PlayerItem OwnedRow(int copies = 1, string itemId = "brass-knuckles")
    {
        var row = NewRow(itemId);
        row.AddCopies(copies);
        return row;
    }

    // -------------------------------------------------------------------------
    // Owning copies
    // -------------------------------------------------------------------------

    [Fact]
    public void A_new_row_owns_nothing_and_is_not_equipped()
    {
        var row = NewRow();

        Assert.Equal(0, row.Quantity);
        Assert.False(row.Equipped);
    }

    [Fact]
    public void Adding_copies_increases_the_quantity_owned()
    {
        var row = NewRow();

        row.AddCopies(1);
        row.AddCopies(3);

        Assert.Equal(4, row.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Adding_a_non_positive_number_of_copies_is_rejected(int count)
    {
        var row = NewRow();

        Assert.Throws<ArgumentOutOfRangeException>(() => row.AddCopies(count));
        Assert.Equal(0, row.Quantity);
    }

    // -------------------------------------------------------------------------
    // Equipping
    // -------------------------------------------------------------------------

    [Fact]
    public void An_owned_item_can_be_equipped()
    {
        var row = OwnedRow();

        row.Equip();

        Assert.True(row.Equipped);
    }

    [Fact]
    public void An_item_the_player_does_not_own_cannot_be_equipped()
    {
        var row = NewRow();

        Assert.Throws<InvalidOperationException>(row.Equip);
        Assert.False(row.Equipped);
    }

    [Fact]
    public void Equipping_an_already_equipped_item_leaves_it_equipped()
    {
        var row = OwnedRow();
        row.Equip();

        row.Equip();

        Assert.True(row.Equipped);
    }

    [Fact]
    public void Unequipping_clears_the_flag_but_keeps_the_copies()
    {
        var row = OwnedRow(copies: 2);
        row.Equip();

        row.Unequip();

        Assert.False(row.Equipped);
        Assert.Equal(2, row.Quantity);
    }

    [Fact]
    public void Unequipping_something_that_is_not_equipped_is_harmless()
    {
        var row = OwnedRow();

        row.Unequip();

        Assert.False(row.Equipped);
    }

    // -------------------------------------------------------------------------
    // Consuming copies (Tier 1 consumable item gate)
    // -------------------------------------------------------------------------

    [Fact]
    public void Consuming_copies_decreases_the_quantity_owned()
    {
        var row = OwnedRow(copies: 5);

        row.Consume(2);

        Assert.Equal(3, row.Quantity);
    }

    [Fact]
    public void Consuming_every_copy_leaves_the_row_at_zero()
    {
        var row = OwnedRow(copies: 2);

        row.Consume(2);

        Assert.Equal(0, row.Quantity);
    }

    [Theory]
    [InlineData(3, false, 3)]
    [InlineData(3, true, 2)]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 0)]
    public void The_spare_quantity_never_includes_the_worn_copy(int copies, bool worn, int expectedSpare)
    {
        // What a job's item gate counts: items a job uses are consumed but are not the worn gear.
        var row = OwnedRow(copies);
        if (worn)
            row.Equip();

        Assert.Equal(expectedSpare, row.SpareQuantity);
    }

    [Fact]
    public void A_row_with_no_copies_has_no_spare_even_if_it_is_somehow_flagged_worn()
    {
        // The state the earlier defect left behind. It must not go negative.
        var row = new PlayerItem { PlayerId = Guid.NewGuid(), ItemId = "switchblade" };
        typeof(PlayerItem).GetProperty(nameof(PlayerItem.Equipped))!.SetValue(row, true);

        Assert.Equal(0, row.SpareQuantity);
    }

    [Fact]
    public void Consuming_the_last_copy_of_a_worn_item_takes_it_off()
    {
        // A worn item the player no longer has must not keep scoring. It used to stay flagged as
        // equipped at zero copies, which kept its attack and defense in the loadout for good.
        var row = OwnedRow(copies: 1);
        row.Equip();

        row.Consume(1);

        Assert.Equal(0, row.Quantity);
        Assert.False(row.Equipped);
    }

    [Fact]
    public void Consuming_some_copies_of_a_worn_item_leaves_it_worn()
    {
        // Spending part of a stack is not taking the item off: the player still holds one to wear.
        var row = OwnedRow(copies: 3);
        row.Equip();

        row.Consume(2);

        Assert.Equal(1, row.Quantity);
        Assert.True(row.Equipped);
    }

    [Fact]
    public void Consuming_the_last_copy_of_an_unworn_item_leaves_it_unworn()
    {
        var row = OwnedRow(copies: 1);

        row.Consume(1);

        Assert.False(row.Equipped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Consuming_a_non_positive_number_of_copies_is_rejected(int count)
    {
        var row = OwnedRow(copies: 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => row.Consume(count));
        Assert.Equal(3, row.Quantity);
    }

    [Fact]
    public void Consuming_more_copies_than_are_held_is_rejected()
    {
        var row = OwnedRow(copies: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => row.Consume(3));
        Assert.Equal(2, row.Quantity);
    }
}
