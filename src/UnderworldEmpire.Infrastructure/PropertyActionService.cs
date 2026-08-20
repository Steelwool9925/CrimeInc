using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Properties;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Infrastructure;

/// <summary>Why a property request could not be carried out, beyond the domain's own refusals.</summary>
public enum PropertyActionError
{
    /// <summary>The action was applied.</summary>
    None = 0,

    /// <summary>No property in the active content pack carries the requested id.</summary>
    UnknownProperty,

    /// <summary>No player row for the authenticated id.</summary>
    UnknownPlayer,

    /// <summary>
    /// A concurrent request modified the same player first. Surfaced as a conflict rather than retried —
    /// see the remarks on <see cref="PropertyActionService"/>.
    /// </summary>
    ConcurrencyConflict
}

/// <summary>The full result of a buy request: a transport-level error, a domain refusal, or a purchase.</summary>
/// <param name="Error">Transport/lookup level problem, or <see cref="PropertyActionError.None"/>.</param>
/// <param name="Result">The domain's answer — carries the refusal reason as well as the success detail.</param>
/// <param name="Player">The player after the purchase. Null unless it succeeded.</param>
/// <param name="Pending">Cash collectable straight after the purchase. Zero unless it succeeded.</param>
/// <param name="Cap">The ceiling after the purchase. Zero unless it succeeded.</param>
public record PropertyPurchaseActionResult(
    PropertyActionError Error,
    PropertyPurchaseResult? Result,
    Player? Player = null,
    long Pending = 0,
    long Cap = 0)
{
    /// <summary>Whether the purchase was applied and persisted.</summary>
    public bool Succeeded => Error == PropertyActionError.None && Result is { Succeeded: true };
}

/// <summary>The full result of a collect request.</summary>
/// <param name="Error">Transport/lookup level problem, or <see cref="PropertyActionError.None"/>.</param>
/// <param name="Result">The domain's answer — what was paid out, or why nothing was.</param>
/// <param name="Player">The player after the collection. Null unless it succeeded.</param>
public record PropertyCollectActionResult(
    PropertyActionError Error,
    PropertyCollectResult? Result,
    Player? Player = null)
{
    /// <summary>Whether the collection was applied and persisted.</summary>
    public bool Succeeded => Error == PropertyActionError.None && Result is { Succeeded: true };
}

/// <summary>
/// Application service for buying income property and collecting its takings: loads the player and the
/// holding the rule needs, delegates to <see cref="PropertyService"/>, and persists the whole change in
/// one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity.</b> Cash and the holding row are written by a single <c>SaveChangesAsync</c>, which EF
/// wraps in one transaction. There is no state where a player was charged but owns nothing, and none
/// where a holding was emptied but the cash never arrived — the second being the one that would cost the
/// player real progress.
/// </para>
/// <para>
/// <b>Concurrency.</b> Same <c>xmin</c> token as <see cref="JobActionService"/> and
/// <see cref="InventoryActionService"/>. Both actions here change <see cref="Player.Cash"/>, so unlike an
/// equip they put the player row in the update batch on their own and need no
/// <c>TouchForConcurrency</c> equivalent. Collecting is the case that makes the token load-bearing: two
/// concurrent collects would otherwise each read the same pending balance and each credit it, paying the
/// takings out twice from one holding.
/// </para>
/// <para>
/// Conflicts are <b>not</b> retried. On a buy a retry would charge twice; on a collect it would settle a
/// holding whose payout the first request already banked. Both are reported so the client re-reads.
/// </para>
/// <para>
/// <b>One clock per request.</b> Each method takes <c>now</c> once and passes that single instant into
/// every call. Reading the clock twice inside one operation would let the settle and the projection
/// disagree by the time between them, which is how a response comes back reporting a pending balance the
/// stored row never held.
/// </para>
/// </remarks>
public class PropertyActionService
{
    private readonly GameDbContext _db;
    private readonly PropertyService _properties;
    private readonly IContentPackProvider _content;

    public PropertyActionService(GameDbContext db, PropertyService properties, IContentPackProvider content)
    {
        _db = db;
        _properties = properties;
        _content = content;
    }

    /// <summary>Buys one unit of <paramref name="propertyId"/> for <paramref name="playerId"/>.</summary>
    public async Task<PropertyPurchaseActionResult> PurchaseAsync(
        Guid playerId,
        string propertyId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        // Resolve content before touching the database: an unknown property id is a client error and
        // needs no round trip to detect.
        var property = _content.PropertyCatalog.Find(propertyId);
        if (property is null)
            return new PropertyPurchaseActionResult(PropertyActionError.UnknownProperty, null);

        // Tracked (not AsNoTracking) — this is a write path.
        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
            return new PropertyPurchaseActionResult(PropertyActionError.UnknownPlayer, null);

        // Primary-key seek on (PlayerId, PropertyId). FindAsync also short-circuits to the change
        // tracker if the row is already loaded.
        var holding = await _db.PlayerProperties.FindAsync([playerId, propertyId], cancellationToken);

        bool isNewHolding = holding is null;
        if (holding is null)
        {
            holding = new PlayerProperty { PlayerId = playerId, PropertyId = propertyId };
            _db.PlayerProperties.Add(holding);
        }

        var result = _properties.Purchase(player, property, holding, nowUtc);

        if (!result.Succeeded)
        {
            // The domain refused and mutated nothing. Detach a holding we speculatively added so a
            // refusal cannot leave an empty row behind on some later SaveChanges — an empty row would
            // also carry a default AccruedAtUtc, handing the eventual first purchase a full property.
            if (isNewHolding)
                _db.Entry(holding).State = EntityState.Detached;

            return new PropertyPurchaseActionResult(PropertyActionError.None, result);
        }

        if (!await TrySaveAsync(cancellationToken))
            return new PropertyPurchaseActionResult(PropertyActionError.ConcurrencyConflict, null);

        return new PropertyPurchaseActionResult(
            PropertyActionError.None,
            result,
            player,
            holding.PendingAt(property, nowUtc),
            holding.CapAt(property));
    }

    /// <summary>Collects everything <paramref name="propertyId"/> has earned for the player.</summary>
    public async Task<PropertyCollectActionResult> CollectAsync(
        Guid playerId,
        string propertyId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var property = _content.PropertyCatalog.Find(propertyId);
        if (property is null)
            return new PropertyCollectActionResult(PropertyActionError.UnknownProperty, null);

        var player = await _db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
        if (player is null)
            return new PropertyCollectActionResult(PropertyActionError.UnknownPlayer, null);

        var holding = await _db.PlayerProperties.FindAsync([playerId, propertyId], cancellationToken);

        // Nothing stored means nothing owned. An empty stand-in gets the domain's "notOwned" refusal
        // without inserting a row for a player who only tapped a property they do not have — and
        // deliberately without adding it to the context, so a refusal writes nothing at all.
        holding ??= new PlayerProperty { PlayerId = playerId, PropertyId = propertyId };

        var result = _properties.Collect(player, property, holding, nowUtc);

        if (!result.Succeeded)
            return new PropertyCollectActionResult(PropertyActionError.None, result);

        if (!await TrySaveAsync(cancellationToken))
            return new PropertyCollectActionResult(PropertyActionError.ConcurrencyConflict, null);

        return new PropertyCollectActionResult(PropertyActionError.None, result, player);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

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
            // Two concurrent first purchases of the same property each try to INSERT the same
            // (PlayerId, PropertyId) row. The loser hits the composite primary key before the player
            // UPDATE's xmin check can matter. Same outcome, different mechanism.
            return false;
        }
    }
}
