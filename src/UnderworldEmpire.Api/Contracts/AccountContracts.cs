namespace UnderworldEmpire.Api.Contracts;

/// <summary>Request body for POST /account/register.</summary>
/// <param name="Username">The desired username. Validated and lower-cased server-side.</param>
/// <param name="Password">The plaintext password. Never stored — only its hash is.</param>
public record RegisterAccountRequest(string Username, string Password);

/// <summary>Request body for POST /account/login.</summary>
/// <param name="Username">The account username. Compared case-insensitively (lower-cased server-side).</param>
/// <param name="Password">The plaintext password, checked against the stored hash.</param>
public record LoginRequest(string Username, string Password);

/// <summary>Response body for POST /account/login.</summary>
/// <param name="DeviceToken">
///     The bearer credential for this player — the same token every other device logged into this
///     account already holds. Login never issues a new one; see the design note in the feature
///     plan for why.
/// </param>
/// <param name="PlayerId">The stable identifier for this player account.</param>
public record LoginResponse(string DeviceToken, Guid PlayerId);
