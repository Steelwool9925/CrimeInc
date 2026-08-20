using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for the item catalog, inventory and loadout (T3.2).</summary>
public static class ItemEndpoints
{
    /// <summary>Registers the item endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /inventory — the catalog as offered to the calling player, with their holdings and
        // affordability folded in. Authenticated (unlike anonymous GET /content/pack) precisely
        // because the response is player-specific.
        app.MapGet("/inventory", [Authorize] async (
            HttpContext ctx,
            GameDbContext db,
            IContentPackProvider content,
            InventoryService inventory) =>
        {
            var playerId = ctx.User.GetPlayerId();

            // Read-only projection, so both reads are AsNoTracking — no change-tracker overhead and
            // no risk of an accidental write from a GET.
            var player = await db.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == playerId);

            if (player is null)
                return Results.NotFound();

            // One query for this player's holdings, keyed for O(1) lookup while projecting. Loading
            // only this player's rows (not a join per item) keeps GET /inventory at two queries
            // total regardless of how many items the pack defines.
            var holdingsByItemId = await db.PlayerItems
                .AsNoTracking()
                .Where(h => h.PlayerId == playerId)
                .ToDictionaryAsync(h => h.ItemId, StringComparer.Ordinal);

            var catalog = content.ItemCatalog;

            var items = new List<InventoryItemDto>(catalog.Items.Count);
            foreach (var item in catalog.Items)
            {
                // A player who owns none of an item is projected against an empty holding rather
                // than a special case, so the gating rules see one shape either way.
                var holding = holdingsByItemId.GetValueOrDefault(item.Id)
                              ?? new PlayerItem { PlayerId = playerId, ItemId = item.Id };

                var buyObstacle = inventory.CheckPurchase(player, item);
                var equipObstacle = inventory.CheckEquip(player, item, holding);

                items.Add(new InventoryItemDto(
                    Id: item.Id,
                    Name: item.Name,
                    Description: item.Description,
                    Slot: SlotToken(item.Slot),
                    LevelRequirement: item.LevelRequirement,
                    Price: item.Price,
                    Attack: item.Attack,
                    Defense: item.Defense,
                    Quantity: holding.Quantity,
                    Equipped: holding.Equipped,
                    CanBuy: buyObstacle == InventoryFailure.None,
                    BuyUnavailableReason: ReasonToken(buyObstacle),
                    CanEquip: equipObstacle == InventoryFailure.None,
                    EquipUnavailableReason: ReasonToken(equipObstacle)));
            }

            var loadout = Loadout.For(player, catalog.ResolveEquipped(holdingsByItemId.Values));

            return Results.Ok(new InventoryResponse(
                Items: items,
                Loadout: PlayerProjection.ToLoadoutDto(loadout),
                Cash: player.Cash,
                Level: player.Level));
        })
        .WithName("GetInventory")
        .WithTags("Items");

        // POST /items/{id}/buy — buy one copy. Like POST /jobs/{id}/do there is no request body:
        // the price is content, so there is nothing for a client to supply or tamper with.
        app.MapPost("/items/{id}/buy", [Authorize] async (
            string id,
            HttpContext ctx,
            InventoryActionService inventoryActions) =>
        {
            var playerId = ctx.User.GetPlayerId();

            var result = await inventoryActions.PurchaseAsync(playerId, id);

            if (Translate(result.Error, id) is { } problem)
                return problem;

            var purchase = result.Result!;
            if (!purchase.Succeeded)
                return Refusal(purchase.Failure);

            return Results.Ok(new BuyItemResponse(
                ItemId: id,
                CashSpent: purchase.CashSpent,
                QuantityOwned: purchase.QuantityOwned,
                Player: PlayerProjection.ToMeDto(result.Player!, DateTimeOffset.UtcNow),
                Loadout: PlayerProjection.ToLoadoutDto(result.Loadout!)));
        })
        .WithName("BuyItem")
        .WithTags("Items");

        // POST /items/{id}/equip — wear it, taking off whatever was in its slot.
        app.MapPost("/items/{id}/equip", [Authorize] async (
            string id,
            HttpContext ctx,
            InventoryActionService inventoryActions) =>
        {
            var playerId = ctx.User.GetPlayerId();

            return EquipOutcome(await inventoryActions.EquipAsync(playerId, id), id);
        })
        .WithName("EquipItem")
        .WithTags("Items");

        // POST /items/{id}/unequip — take it off, keeping the copies owned.
        app.MapPost("/items/{id}/unequip", [Authorize] async (
            string id,
            HttpContext ctx,
            InventoryActionService inventoryActions) =>
        {
            var playerId = ctx.User.GetPlayerId();

            return EquipOutcome(await inventoryActions.UnequipAsync(playerId, id), id);
        })
        .WithName("UnequipItem")
        .WithTags("Items");

        return app;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Renders an equip or unequip result, which differ only in which side of the swap is populated.
    /// </summary>
    private static IResult EquipOutcome(ItemEquipActionResult result, string itemId)
    {
        if (Translate(result.Error, itemId) is { } problem)
            return problem;

        var action = result.Result!;
        if (!action.Succeeded)
            return Refusal(action.Failure);

        return Results.Ok(new EquipItemResponse(
            EquippedItemId: action.EquippedItemId,
            UnequippedItemId: action.UnequippedItemId,
            Player: PlayerProjection.ToMeDto(result.Player!, DateTimeOffset.UtcNow),
            Loadout: PlayerProjection.ToLoadoutDto(result.Loadout!)));
    }

    /// <summary>
    /// Maps a transport-level error to its response, or <see langword="null"/> when there was none.
    /// </summary>
    private static IResult? Translate(InventoryActionError error, string itemId) => error switch
    {
        InventoryActionError.UnknownItem => Results.Problem(
            detail: $"No item with id '{itemId}' exists in the active content pack.",
            statusCode: StatusCodes.Status404NotFound),

        InventoryActionError.UnknownPlayer => Results.NotFound(),

        // Deliberately not retried — a retry would charge the player a second time, which is the
        // duplicate the concurrency token exists to prevent.
        InventoryActionError.ConcurrencyConflict => Results.Problem(
            detail: "Another request modified your state first. Re-read and try again.",
            statusCode: StatusCodes.Status409Conflict),

        _ => null
    };

    /// <summary>
    /// Renders a domain refusal as a 409 carrying the same reason token GET /inventory reports, so one
    /// client code path handles a refusal whether it was predicted or hit.
    /// </summary>
    private static IResult Refusal(InventoryFailure failure)
    {
        var detail = failure switch
        {
            InventoryFailure.LevelTooLow => "Your level is too low for this item.",
            InventoryFailure.InsufficientCash => "You cannot afford this item.",
            InventoryFailure.NotOwned => "You do not own this item.",
            InventoryFailure.AlreadyEquipped => "You are already wearing this item.",
            InventoryFailure.NotEquipped => "You are not wearing this item.",
            _ => "The action could not be completed."
        };

        return Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["reason"] = ReasonToken(failure) });
    }

    /// <summary>
    /// The wire token for <paramref name="slot"/>, matching the vocabulary <c>items.json</c> uses.
    /// </summary>
    private static string SlotToken(ItemSlot slot) => slot switch
    {
        ItemSlot.Weapon => "weapon",
        ItemSlot.Armor => "armor",
        ItemSlot.Vehicle => "vehicle",
        _ => slot.ToString().ToLowerInvariant()
    };

    /// <summary>
    /// The stable reason token for <paramref name="failure"/>, or <see langword="null"/> when there is no
    /// obstacle.
    /// </summary>
    /// <remarks>
    /// One mapping serves the listing and the action endpoints, so a refusal a client sees on a button is
    /// the same token it gets back if it presses anyway. <c>"level"</c> matches the token GET /jobs already
    /// uses for the same obstacle.
    /// </remarks>
    internal static string? ReasonToken(InventoryFailure failure) => failure switch
    {
        InventoryFailure.None => null,
        InventoryFailure.LevelTooLow => "level",
        InventoryFailure.InsufficientCash => "cash",
        InventoryFailure.NotOwned => "notOwned",
        InventoryFailure.AlreadyEquipped => "alreadyEquipped",
        InventoryFailure.NotEquipped => "notEquipped",
        _ => "unknown"
    };
}
