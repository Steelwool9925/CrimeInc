namespace UnderworldEmpire.Domain.Daily;

/// <summary>Why a daily claim was refused, or <see cref="None"/> when it paid.</summary>
public enum DailyFailure
{
    /// <summary>The claim paid.</summary>
    None = 0,

    /// <summary>It was already claimed today.</summary>
    AlreadyClaimedToday
}
