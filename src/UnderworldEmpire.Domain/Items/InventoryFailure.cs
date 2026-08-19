namespace UnderworldEmpire.Domain.Items;

/// <summary>
/// Why an inventory action was refused, or <see cref="None"/> when it succeeded.
/// </summary>
/// <remarks>
/// These are ordinary answers in normal play — a player taps gear they cannot afford constantly — so
/// they are returned, not thrown, exactly as <see cref="Jobs.JobFailure"/> is. The names become the
/// <c>reason</c> token the endpoints (T3.2) report, so one client code path can handle a refusal from
/// buying or equipping.
/// </remarks>
public enum InventoryFailure
{
    /// <summary>The action succeeded.</summary>
    None = 0,

    /// <summary>The player's level is below the item's requirement. Permanent until they level.</summary>
    LevelTooLow,

    /// <summary>The player cannot afford the price. Temporary — they can earn more.</summary>
    InsufficientCash,

    /// <summary>The player owns no copy of the item, so it cannot be equipped.</summary>
    NotOwned,

    /// <summary>The item is already worn; equipping it again would be a no-op.</summary>
    AlreadyEquipped,

    /// <summary>The item is not worn, so there is nothing to take off.</summary>
    NotEquipped
}
