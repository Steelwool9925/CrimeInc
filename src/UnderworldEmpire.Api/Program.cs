using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using UnderworldEmpire.Api.Auth;
using UnderworldEmpire.Api.Endpoints;
using UnderworldEmpire.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Bearer auth: device-token lookup against GameDbContext.
builder.Services
    .AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, DeviceTokenAuthHandler>("Bearer", null);

// No global fallback policy — /health and /session must remain anonymous.
builder.Services.AddAuthorization();

// Dev CORS: the Flutter web build runs on a different origin (its own dev-server port),
// so browser requests to this API are cross-origin. Wide-open in Development only.
builder.Services.AddCors(options =>
    options.AddPolicy("dev", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// docs/hosting-a-friend.md's own hosting story (run-api-hosted.ps1 + a cloudflared/ngrok tunnel)
// connects every remote player to Kestrel via localhost — without this, Connection.RemoteIpAddress
// is the SAME loopback address for all of them, and the per-IP rate limiter below would partition
// every friend into one shared bucket: one mistyped password locking out everyone. The default
// ForwardedHeadersOptions trusts X-Forwarded-For only from that same loopback hop (KnownProxies
// defaults to 127.0.0.1/::1) — exactly the tunnel-to-Kestrel hop this setup creates, so this does
// not need (and must not get) a wider KnownProxies/KnownNetworks: widening it would let anyone who
// reaches this port directly spoof their apparent IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Fixed-window rate limiting on login, keyed per client IP: 5 attempts per 5 minutes.
// Internet-exposed password auth is the highest-value brute-force target in this API; this is
// the mandatory mitigation named in the feature plan, not optional hardening.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return ValueTask.CompletedTask;
    };
});

// Swagger UI (served at /swagger in Development) is the manual API test surface used
// throughout the plan's session checkpoints.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Paste your device token (without the 'Bearer ' prefix).",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            []
        }
    });
});

var app = builder.Build();

// Must run before anything that reads the connection's remote IP (the rate limiter below,
// primarily) — per Microsoft's own guidance, forwarded-header processing belongs first in the
// pipeline so every later middleware sees the corrected value.
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(); // UI at /swagger
    app.UseCors("dev");
}
else
{
    // Only enforce HTTPS redirect outside Development so the local Flutter app can
    // talk plain HTTP to the dev server without redirect/cert friction.
    app.UseHttpsRedirection();
}

// Auth middleware must appear before endpoint mapping.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Liveness: 200 whenever the process is up. This is the CP0 check.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health")
   .WithTags("Diagnostics");

// Readiness: verifies the database is reachable (needs Postgres running).
app.MapGet("/health/db", async (GameDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? Results.Ok(new { status = "ok", database = "reachable" })
        : Results.Problem("Database unreachable", statusCode: StatusCodes.Status503ServiceUnavailable);
}).WithName("HealthDb").WithTags("Diagnostics");

app.MapSessionEndpoints();
app.MapAccountEndpoints();
app.MapContentEndpoints();
app.MapMeEndpoints();
app.MapJobEndpoints();
app.MapSkillEndpoints();
app.MapItemEndpoints();
app.MapPropertyEndpoints();

app.Run();

// Exposed so integration tests can spin up the host via WebApplicationFactory.
public partial class Program { }
