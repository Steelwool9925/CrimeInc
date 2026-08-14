using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Api.Auth;

/// <summary>
/// Custom authentication handler that reads the <c>Authorization: Bearer &lt;token&gt;</c> header,
/// looks up the player by DeviceToken via <see cref="GameDbContext"/>, and on a match produces a
/// <see cref="ClaimsPrincipal"/> carrying <see cref="ClaimTypes.NameIdentifier"/> = playerId.
/// On no match the handler returns <see cref="AuthenticateResult.NoResult"/> so that anonymous
/// endpoints (/health, /session) are unaffected.
/// </summary>
public sealed class DeviceTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly GameDbContext _db;

    public DeviceTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        GameDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var header = authHeader.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.Fail("Empty bearer token.");

        var player = await _db.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.DeviceToken == token);

        if (player is null)
            return AuthenticateResult.Fail("Unknown device token.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, player.Id.ToString())
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
