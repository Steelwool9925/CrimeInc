namespace UnderworldEmpire.Domain.Properties;

/// <summary>
/// Why a property action was refused, or <see cref="None"/> when it succeeded.
/// </summary>
/// <remarks>
/// Ordinary answers in normal play — a player taps a property they cannot afford, or a collect button a
/// second after the last one — so they are returned, not thrown, exactly as
/// <see cref="Items.InventoryFailure"/> is. The names become the <c>reason</c> token the endpoints
/// (T3.4) report.
/// </remarks>
public enum PropertyFailure
{
    /// <summary>The action succeeded.</summary>
    None = 0,

    /// <summary>The player's level is below the property's requirement. Permanent until they level.</summary>
    LevelTooLow,

    /// <summary>The player cannot afford the price. Temporary — they can earn more.</summary>
    InsufficientCash,

    /// <summary>The player owns no unit of the property, so there is nothing to collect from.</summary>
    NotOwned,

    /// <summary>The property owns units but has not yet earned a whole coin since the last collection.</summary>
    NothingToCollect
}
