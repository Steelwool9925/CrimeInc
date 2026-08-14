using System.Security.Claims;

namespace UnderworldEmpire.Api.Auth;

/// <summary>Extension helpers for reading typed values from a <see cref="ClaimsPrincipal"/>.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Returns the player's <see cref="Guid"/> from the
    /// <see cref="ClaimTypes.NameIdentifier"/> claim.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the claim is absent or cannot be parsed as a <see cref="Guid"/>.
    /// </exception>
    public static Guid GetPlayerId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("NameIdentifier claim is missing.");

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException($"NameIdentifier claim '{value}' is not a valid GUID.");
    }
}
