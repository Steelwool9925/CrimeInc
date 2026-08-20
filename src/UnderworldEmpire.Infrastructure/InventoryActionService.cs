using Microsoft.EntityFrameworkCore;
using Npgsql;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Infrastructure;

/// <summary>Why an inventory request could not be carried out, beyond the domain's own refusals.</summary>
public enum InventoryActionError
{
    /// <summary>The action was applied.</summary>
    None = 0,

    /// <summary>No item in the active content pack carries the requested id.</summary>
    UnknownItem,

    /// <summary>No player row for the authenticated id.</summary>
    UnknownPlayer,

    /// <summary>
    /// A concurrent request modified the same player first. Surfaced as a conflict rather than retried —
    /// see the remarks on <see cref="InventoryActionService"/>.
    /// </summary>
    ConcurrencyConflict
}

/// <summary>The full result of a buy request: a transport-level error, a domain refusal, or a purchase.</summary>
/// <param name="Error">Transport/lookup level problem, or <see cref="InventoryActionError.None"/>.</param>
/// <param name="Result">The domain's answer — carries the refusal reason as well as the success detail.</param>
/// <param name="Player">The player after the purchase. Null unless it succeeded.</param>
/// <param name="Loadout">Effective stats after the purchase. Null unless it succeeded.</param>
public record ItemPurchaseActionResult(
    InventoryActionError Error,
    ItemPurchaseResult? Result,
    Player? Player = null,
    Loadout? Loadout = null)
{
    /// <summary>Whether the purchase was applied and persisted.</summary>
    public bool Succeeded => Error == InventoryActionError.None && Result is { Succeeded: true };
}

/// <summary>The full result of an equip or unequip request.</summary>
/// <param name="Error">Transport/lookup level problem, or <see cref="InventoryActionError.None"/>.</param>
/// <param name="Result">The domain's answer — which items changed state, or why none did.</param>
/// <param name="Player">The player after the action. Null unless it succeeded.</param>
/// <param name="Loadout">Effective stats after the action — the figure CP3 watches rise.</param>
public record ItemEquipActionResult(
    InventoryActionError Error,
    ItemEquipResult? Result,
    Player? Player = null,
    Loadout? Loadout = null)
{
    /// <summary>Whether the action was applied and persisted.</summary>
    public bool Succeeded => Error == InventoryActionError.None && Result is { Succeeded: true };
}

/// <summary>
/// Application service for buying and wearing gear: loads the player and the holdings the rule needs,
/// delegates to <see cref="InventoryService"/>, and persists the whole change in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity.</b> Cash and the holding row are written by a single <c>SaveChangesAsync</c>, which EF
/// wraps in one transaction. There is no state where a player was charged but owns nothing.
/// </para>
/// <para>
/// <b>Concurrency.</b> Same <c>xmin</c> token as <see cref="JobActionService"/>, and for the same reason:
/// without it two concurrent buys could each read the same cash balance and each deduct from it, handing
/// the player a second item for free. Conflicts are <b>not</b> retried — a retry would charge twice — so
/// they are reported and the client re-reads.
/// </para>
/// <para>
/// Equipping is protected the same way even though it changes nothing on the player. See
/// <see cref="TouchForConcurrency"/>: without that, two simultaneous equips of two different weapons
/// would each see an empty weapon slot, neither would displace the other, and the player would end up
/// wearing both — a state <see cref="Loadout.For"/> refuses to aggregate, so it would show as a 500 on
/// every subsequent read of their own inventory.
/// </para>
/// </remarks>
public class InventoryActionService
{
    private readonly GameDbContext _db;
    private readonly InventoryService _inventory;
    private readonly IContentPackProvider _content;

    public InventoryActionService(GameDbContext db, InventoryService inventory, IContentPackProvider content)
    {
        _db = db;
        _inventory = inventory;
        _content = content;
    }

    /// <summary>Buys one copy of <paramref name="itemId"/> for <paramref name="playerId"/>.</summary>
    public async Task<ItemPurchaseActionResult> PurchaseAsync(
        Guid playerId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var catalog = _content.ItemCatalog;

        // Resolve content before touching the database: an unknown item id is a client error and needs
        // no round trip to detect.
        var item = catalog.Find(itemId);
        if (item is null)
            return new ItemPurchaseActionResult(InventoryActionError.UnknownItem, null);

        // Tracked (not AsNoTracking) — this is a write path.
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
            return new ItemPurchaseActionResult(InventoryActionError.UnknownPlayer, null);

        // Primary-key seek on (PlayerId, ItemId). FindAsync also short-circuits to the change tracker if
        // the row is already loaded, which matters when a caller buys twice in one unit of work.
        var holding = await _db.PlayerItems.FindAsync([playerId, itemId], cancellationToken);

        bool isNewHolding = holding is null;
        if (holding is null)
        {
            holding = new PlayerItem { PlayerId = playerId, ItemId = itemId };
            _db.PlayerItems.Add(holding);
        }

        var result = _inventory.Purchase(player, item, holding);

        if (!result.Succeeded)
        {
            // The domain refused and mutated nothing. Detach a holding we speculatively added so a
            // refusal cannot leave an empty row behind on some later SaveChanges.
            if (isNewHolding)
                _db.Entry(holding).State = EntityState.Detached;

            return new ItemPurchaseActionResult(InventoryActionError.None, result);
        }

        if (!await TrySaveAsync(cancellationToken))
            return new ItemPurchaseActionResult(InventoryActionError.ConcurrencyConflict, null);

        return new ItemPurchaseActionResult(
            InventoryActionError.None,
            result,
            player,
            await BuildLoadoutAsync(player, cancellationToken));
    }

    /// <summary>Wears <paramref name="itemId"/>, displacing whatever occupied its slot.</summary>
    public async Task<ItemEquipActionResult> EquipAsync(
        Guid playerId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var catalog = _content.ItemCatalog;

        var item = catalog.Find(itemId);
        if (item is null)
            return new ItemEquipActionResult(InventoryActionError.UnknownItem, null);

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
            return new ItemEquipActionResult(InventoryActionError.UnknownPlayer, null);

        // Every holding, not only the worn ones: the domain needs the target's row and the slot's
        // incumbent, and one query for both beats two. A player's inventory is bounded by the catalog.
        var holdings = await _db.PlayerItems
            .Where(h => h.PlayerId == playerId)
            .ToListAsync(cancellationToken);

        var holding = holdings.FirstOrDefault(h => string.Equals(h.ItemId, itemId, StringComparison.Ordinal))
                      ?? new PlayerItem { PlayerId = playerId, ItemId = itemId };

        var result = _inventory.Equip(player, item, holding, Resolve(holdings, catalog));

        if (!result.Succeeded)
            return new ItemEquipActionResult(InventoryActionError.None, result);

        TouchForConcurrency(player);

        if (!await TrySaveAsync(cancellationToken))
            return new ItemEquipActionResult(InventoryActionError.ConcurrencyConflict, null);

        return new ItemEquipActionResult(
            InventoryActionError.None,
            result,
            player,
            Loadout.For(player, catalog.ResolveEquipped(holdings)));
    }

    /// <summary>Takes off <paramref name="itemId"/>, keeping the copies owned.</summary>
    public async Task<ItemEquipActionResult> UnequipAsync(
        Guid playerId,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var catalog = _content.ItemCatalog;

        // Unequipping needs no item definition, but an id that names nothing is still a client error and
        // is reported as one rather than as "you are not wearing that".
        if (catalog.Find(itemId) is null)
            return new ItemEquipActionResult(InventoryActionError.UnknownItem, null);

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
            return new ItemEquipActionResult(InventoryActionError.UnknownPlayer, null);

        var holdings = await _db.PlayerItems
            .Where(h => h.PlayerId == playerId)
            .ToListAsync(cancellationToken);

        var holding = holdings.FirstOrDefault(h => string.Equals(h.ItemId, itemId, StringComparison.Ordinal))
                      ?? new PlayerItem { PlayerId = playerId, ItemId = itemId };

        var result = _inventory.Unequip(holding);

        if (!result.Succeeded)
            return new ItemEquipActionResult(InventoryActionError.None, result);

        TouchForConcurrency(player);

        if (!await TrySaveAsync(cancellationToken))
            return new ItemEquipActionResult(InventoryActionError.ConcurrencyConflict, null);

        return new ItemEquipActionResult(
            InventoryActionError.None,
            result,
            player,
            Loadout.For(player, catalog.ResolveEquipped(holdings)));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Pairs each holding with its definition, dropping ids the catalog no longer knows.</summary>
    private static List<OwnedItem> Resolve(IEnumerable<PlayerItem> holdings, ItemCatalog catalog)
    {
        var owned = new List<OwnedItem>();
        foreach (var holding in holdings)
        {
            if (catalog.Find(holding.ItemId) is { } definition)
                owned.Add(new OwnedItem(holding, definition));
        }

        return owned;
    }

    /// <summary>Reads the player's equipped holdings and aggregates them into a <see cref="Loadout"/>.</summary>
    private async Task<Loadout> BuildLoadoutAsync(Player player, CancellationToken cancellationToken)
    {
        var equipped = await _db.PlayerItems
            .Where(h => h.PlayerId == player.Id && h.Equipped)
            .ToListAsync(cancellationToken);

        return Loadout.For(player, _content.ItemCatalog.ResolveEquipped(equipped));
    }

    /// <summary>
    /// Forces the player row into the update batch so its <c>xmin</c> concurrency token is checked, even
    /// though nothing on the player changed.
    /// </summary>
    /// <remarks>
    /// Equipping writes only <c>PlayerItems</c>, and those rows carry no concurrency token — the whole
    /// invariant they violate (one worn item per slot) spans rows rather than living in one. Making the
    /// player row part of every equip turns it into the serialisation point: the second of two concurrent
    /// equips re-evaluates its <c>WHERE ... AND xmin = @original</c> after the first commits, matches
    /// nothing, and is reported as a conflict instead of quietly leaving the player wearing two weapons.
    /// <para>
    /// Every value written is the value already there, so this is a no-op to the data — it exists purely to
    /// put the row in the statement batch and so bring its token with it. A dedicated "version" column would
    /// be a second concurrency mechanism alongside the one Postgres already gives us for free.
    /// </para>
    /// </remarks>
    private void TouchForConcurrency(Player player)
    {
        _db.Entry(player).State = EntityState.Modified;
    }

    /// <summary>Saves, translating both flavours of write conflict into <see langword="false"/>.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The xmin token fired: another request updated this player between our read and write.
            return false;
        }
        catch (DbUpdateException ex) when (WriteConflicts.IsUniqueViolation(ex))
        {
            // Two concurrent first purchases of the same item each try to INSERT the same
            // (PlayerId, ItemId) row. The loser hits the composite primary key, not the concurrency
            // token — the INSERT fails before the player UPDATE's xmin check can matter. Same outcome,
            // different mechanism, so it reports the same conflict rather than surfacing as a 500.
            // (This is the M2 mastery-row race, in the shape T3.2 gives it.)
            return false;
        }
    }

}
