namespace UnderworldEmpire.Api.Contracts;

/// <summary>Request body for POST /session. DeviceToken is optional; omit or leave null to create a new session.</summary>
/// <param name="DeviceToken">An existing device token to resume a prior session, or <see langword="null"/> to request a new one.</param>
public record SessionRequest(string? DeviceToken);

/// <summary>Response body for POST /session.</summary>
/// <param name="DeviceToken">The bearer credential to use on all subsequent authenticated requests.</param>
/// <param name="PlayerId">The stable identifier for this player account.</param>
public record SessionResponse(string DeviceToken, Guid PlayerId);
