using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> that redirects the DbContext to a
/// dedicated test database (<c>underworld_test</c>) and applies EF migrations once at
/// startup so integration tests run against a real schema without touching the dev DB.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
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
