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
}
