using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Contracts;
using UnderworldEmpire.Domain.Properties;
using UnderworldEmpire.Infrastructure;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints for the property catalog, holdings and income (T3.4).</summary>
public static class PropertyEndpoints
{
    /// <summary>Registers the property endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapPropertyEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /properties — the catalog as offered to the calling player, with their holdings, what
        // those holdings have earned, and affordability folded in. Authenticated (unlike anonymous
        // GET /content/pack) precisely because the response is player-specific.
        app.MapGet("/properties", [Authorize] async (
            HttpContext ctx,
            GameDbContext db,
            IContentPackProvider content,
            PropertyService properties) =>
        {
            var playerId = ctx.User.GetPlayerId();

            // One instant for the whole projection. Reading the clock per property would let the
            // first row in the list be evaluated at a different time from the last, so two units of
            // the same property could report inconsistent pending balances in one response.
            var nowUtc = DateTimeOffset.UtcNow;

            // Read-only projection, so both reads are AsNoTracking — no change-tracker overhead and
            // no risk of an accidental write from a GET.
            //
            // This endpoint deliberately does NOT settle the holdings it reports. Pending income is
            // derived from elapsed time and read through PendingAt, which banks nothing: a polling
            // client hits this route constantly, and a read that settled would reset each holding's
            // clock and discard the part-earned coin every time. Income is stored only when the
            // player actually buys or collects.
            var player = await db.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == playerId);

            if (player is null)
                return Results.NotFound();

            // One query for this player's holdings, keyed for O(1) lookup while projecting, so the
            // endpoint stays at two queries regardless of how many properties the pack defines.
            var holdingsByPropertyId = await db.PlayerProperties
                .AsNoTracking()
                .Where(h => h.PlayerId == playerId)
                .ToDictionaryAsync(h => h.PropertyId, StringComparer.Ordinal);

            var catalog = content.PropertyCatalog;

            var dtos = new List<PropertyDto>(catalog.Properties.Count);
            foreach (var property in catalog.Properties)
            {
                // A player who owns none of a property is projected against an empty holding rather
                // than a special case, so the gating rules see one shape either way.
                var holding = holdingsByPropertyId.GetValueOrDefault(property.Id)
                              ?? new PlayerProperty { PlayerId = playerId, PropertyId = property.Id };

                var buyObstacle = properties.CheckPurchase(player, property, holding.Quantity);
                var collectObstacle = properties.CheckCollect(property, holding, nowUtc);

                dtos.Add(new PropertyDto(
                    Id: property.Id,
                    Name: property.Name,
                    Description: property.Description,
                    LevelRequirement: property.LevelRequirement,
                    Price: property.Price,
                    IncomePerHour: property.IncomePerHour,
                    AccrualCapMinutes: property.AccrualCapMinutes,
                    Quantity: holding.Quantity,
                    MaxUnits: property.MaxUnits == int.MaxValue ? null : property.MaxUnits,
                    Pending: holding.PendingAt(property, nowUtc),
                    Cap: holding.CapAt(property),
                    CanBuy: buyObstacle == PropertyFailure.None,
                    BuyUnavailableReason: ReasonToken(buyObstacle),
                    CanCollect: collectObstacle == PropertyFailure.None,
                    CollectUnavailableReason: ReasonToken(collectObstacle)));
            }

            return Results.Ok(new PropertyListResponse(
                Properties: dtos,
                Cash: player.Cash,
                Level: player.Level));
        })
        .WithName("GetProperties")
        .WithTags("Properties");

        // POST /properties/{id}/buy — buy one unit. Like POST /items/{id}/buy there is no request
        // body: the price is content, so there is nothing for a client to supply or tamper with.
        app.MapPost("/properties/{id}/buy", [Authorize] async (
            string id,
            HttpContext ctx,
            PropertyActionService propertyActions) =>
        {
            var playerId = ctx.User.GetPlayerId();
            var nowUtc = DateTimeOffset.UtcNow;

            var result = await propertyActions.PurchaseAsync(playerId, id, nowUtc);

            if (Translate(result.Error, id) is { } problem)
                return problem;

            var purchase = result.Result!;
            if (!purchase.Succeeded)
                return Refusal(purchase.Failure);

            return Results.Ok(new BuyPropertyResponse(
                PropertyId: id,
                CashSpent: purchase.CashSpent,
                QuantityOwned: purchase.QuantityOwned,
                Pending: result.Pending,
                Cap: result.Cap,
                Player: PlayerProjection.ToMeDto(result.Player!, nowUtc)));
        })
        .WithName("BuyProperty")
        .WithTags("Properties");

        // POST /properties/{id}/collect — bank everything this property has earned.
        app.MapPost("/properties/{id}/collect", [Authorize] async (
            string id,
            HttpContext ctx,
            PropertyActionService propertyActions) =>
        {
            var playerId = ctx.User.GetPlayerId();
            var nowUtc = DateTimeOffset.UtcNow;

            var result = await propertyActions.CollectAsync(playerId, id, nowUtc);

            if (Translate(result.Error, id) is { } problem)
                return problem;

            var collection = result.Result!;
            if (!collection.Succeeded)
                return Refusal(collection.Failure);

            return Results.Ok(new CollectPropertyResponse(
                PropertyId: id,
                CashCollected: collection.CashCollected,
                Pending: collection.PendingAfter,
                Cap: collection.Cap,
                Player: PlayerProjection.ToMeDto(result.Player!, nowUtc)));
        })
        .WithName("CollectProperty")
        .WithTags("Properties");

        return app;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a transport-level error to its response, or <see langword="null"/> when there was none.
    /// </summary>
    private static IResult? Translate(PropertyActionError error, string propertyId) => error switch
    {
        PropertyActionError.UnknownProperty => Results.Problem(
            detail: $"No property with id '{propertyId}' exists in the active content pack.",
            statusCode: StatusCodes.Status404NotFound),

        PropertyActionError.UnknownPlayer => Results.NotFound(),

        // Deliberately not retried — on a buy a retry would charge twice, and on a collect it would
        // settle a holding whose payout the winning request already banked.
        PropertyActionError.ConcurrencyConflict => Results.Problem(
            detail: "Another request modified your state first. Re-read and try again.",
            statusCode: StatusCodes.Status409Conflict),

        _ => null
    };

    /// <summary>
    /// Renders a domain refusal as a 409 carrying the same reason token GET /properties reports, so one
    /// client code path handles a refusal whether it was predicted or hit.
    /// </summary>
    private static IResult Refusal(PropertyFailure failure)
    {
        var detail = failure switch
        {
            PropertyFailure.LevelTooLow => "Your level is too low for this property.",
            PropertyFailure.InsufficientCash => "You cannot afford this property.",
            PropertyFailure.NotOwned => "You do not own this property.",
            PropertyFailure.NothingToCollect => "This property has not earned anything yet.",
            _ => "The action could not be completed."
        };

        return Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["reason"] = ReasonToken(failure) });
    }

    /// <summary>
    /// The stable reason token for <paramref name="failure"/>, or <see langword="null"/> when there is no
    /// obstacle.
    /// </summary>
    /// <remarks>
    /// One mapping serves the listing and the action endpoints, so a refusal a client sees on a button is
    /// the same token it gets back if it presses anyway. <c>"level"</c> and <c>"cash"</c> match the tokens
    /// GET /jobs and GET /inventory already use for the same obstacles.
    /// </remarks>
    internal static string? ReasonToken(PropertyFailure failure) => failure switch
    {
        PropertyFailure.None => null,
        PropertyFailure.LevelTooLow => "level",
        PropertyFailure.InsufficientCash => "cash",
        PropertyFailure.AtUnitLimit => "limit",
        PropertyFailure.NotOwned => "notOwned",
        PropertyFailure.NothingToCollect => "nothingToCollect",
        _ => "unknown"
    };
}
