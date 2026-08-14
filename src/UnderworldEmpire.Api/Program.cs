using UnderworldEmpire.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Swagger UI (served at /swagger in Development) is the manual API test surface used
// throughout the plan's session checkpoints.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(); // UI at /swagger
}

app.UseHttpsRedirection();

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

app.Run();

// Exposed so integration tests can spin up the host via WebApplicationFactory.
public partial class Program { }
