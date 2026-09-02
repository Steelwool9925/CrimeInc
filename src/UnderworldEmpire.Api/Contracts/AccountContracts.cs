namespace UnderworldEmpire.Api.Contracts;

/// <summary>Request body for POST /account/register.</summary>
/// <param name="Username">The desired username. Validated and lower-cased server-side.</param>
/// <param name="Password">The plaintext password. Never stored — only its hash is.</param>
public record RegisterAccountRequest(string Username, string Password);
