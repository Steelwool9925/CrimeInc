namespace UnderworldEmpire.Domain.Progression;

/// <summary>
/// The stats a skill point may be spent on.
/// </summary>
/// <remarks>
/// An enum rather than a string so an unknown target cannot be smuggled through the API and reach a
/// property setter by name. Parsing happens once at the boundary and everything downstream works
/// with a value that is necessarily valid.
/// </remarks>
public enum SkillTarget
{
    /// <summary>Offensive stat, used by combat in M4.</summary>
    Attack,

    /// <summary>Defensive stat, used by combat in M4.</summary>
    Defense
}
