namespace UnderworldEmpire.Domain.Daily;

/// <summary>Why a claim was refused, or <see cref="None"/> when it paid.</summary>
public enum ObjectiveFailure
{
    /// <summary>The claim paid.</summary>
    None = 0,

    /// <summary>The objective is not among today's, or is not one this player was offered.</summary>
    NotToday,

    /// <summary>The target has not been reached yet.</summary>
    NotComplete,

    /// <summary>It was already claimed today.</summary>
    AlreadyClaimed
}
