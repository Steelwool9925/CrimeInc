using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain.Randomness;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> that redirects the DbContext to a
/// dedicated test database (<c>underworld_test</c>) and applies EF migrations once at
/// startup so integration tests run against a real schema without touching the dev DB.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The RNG this host uses. Always-minimum by default; a test that needs the other branch swaps
    /// it for a scripted sequence via <see cref="SwitchableRandomSource.Scripted"/>.
    /// </summary>
    public SwitchableRandomSource Random { get; } = new();

    private const string TestConnectionString =
        "Host=localhost;Port=5433;Database=underworld_test;Username=underworld;Password=underworld";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Override the connection string before the host builds its service provider.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = TestConnectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the existing GameDbContext registration added by AddInfrastructure.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<GameDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<GameDbContext>(options =>
                options.UseNpgsql(TestConnectionString));

            // Pin the RNG. Since M3R every job attempt rolls for success first, so under the real
            // SystemRandomSource a test that performs ten jobs and expects ten completions is a
            // coin flip rather than an assertion — the shipped cheapest job succeeds about 90% of
            // the time, so ten in a row lands roughly a third of the time.
            //
            // AlwaysMin makes the success roll 1, which clears any chance above zero, and pins each
            // cash payout to the job's minimum. Endpoint tests therefore exercise progression
            // deterministically; the failure path gets its own endpoint coverage with a scripted
            // source rather than being left to luck here.
            var random = services.SingleOrDefault(d => d.ServiceType == typeof(IRandomSource));
            if (random is not null)
                services.Remove(random);

            services.AddSingleton<IRandomSource>(Random);
        });

        builder.UseEnvironment("Test");
    }

    /// <summary>
    /// Applies EF migrations to the test database. Call once per test run / fixture.
    /// </summary>
    public void MigrateDb()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();
        db.Database.Migrate();
    }
}
