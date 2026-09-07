using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Domain.Items;
using UnderworldEmpire.Domain.Jobs;
using UnderworldEmpire.Domain.Properties;
using UnderworldEmpire.Domain.Randomness;
using UnderworldEmpire.Infrastructure.Content;
using UnderworldEmpire.Infrastructure.Randomness;

namespace UnderworldEmpire.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Local-dev fallback connection string, matching <c>docker-compose.yml</c>. Uses host
    /// port 5433 to avoid colliding with a natively-installed PostgreSQL on the default 5432.
    /// Real environments MUST override it via the <c>ConnectionStrings__Default</c> environment
    /// variable (ASP.NET Core binds double-underscore env vars into configuration). No
    /// production secret is ever committed.
    /// </summary>
    public const string LocalDevConnectionString =
        "Host=localhost;Port=5433;Database=underworld;Username=underworld;Password=underworld";

    /// <summary>Registers the DbContext (PostgreSQL via Npgsql), infrastructure services, and the content-pack provider.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? LocalDevConnectionString;
        services.AddDbContext<GameDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<EconomyService>();

        // Pure game-rule services. JobService holds no state beyond its RNG, but is registered
        // scoped alongside the services that consume it for lifetime uniformity.
        services.AddScoped<JobService>();
        services.AddScoped<JobActionService>();
        services.AddScoped<ProgressionService>();

        // Stateless, but registered scoped alongside the other rule services for lifetime uniformity.
        services.AddScoped<InventoryService>();
        services.AddScoped<InventoryActionService>();
        services.AddScoped<PropertyService>();
        services.AddScoped<PropertyActionService>();

        // Server-side RNG for game outcomes. Singleton: Random.Shared is thread-safe and holds no
        // per-request state.
        services.AddSingleton<IRandomSource, SystemRandomSource>();

        // Content-pack: load the default pack and its job catalog at startup and register as a
        // singleton. The content root is overridable via "Content:Root" for tests and deployments.
        // Loading here (rather than lazily) means invalid content fails the host at startup instead
        // of surfacing as a 500 on the first request that touches it.
        var contentRoot = configuration["Content:Root"];
        var loader = new ContentPackLoader(contentRoot);
        var pack = loader.Load(BuildInfo.DefaultContentPack);
        // Items load before jobs so a job's itemRequirements can be checked against real item ids —
        // the one place these two otherwise-independent catalogs cross. See LoadJobs's itemCatalog
        // remarks.
        var itemCatalog = loader.LoadItems(BuildInfo.DefaultContentPack);
        var jobCatalog = loader.LoadJobs(BuildInfo.DefaultContentPack, itemCatalog);
        var propertyCatalog = loader.LoadProperties(BuildInfo.DefaultContentPack);
        services.AddSingleton<IContentPackProvider>(
            new ContentPackProvider(
                pack,
                loader.PackRoot(BuildInfo.DefaultContentPack),
                jobCatalog,
                itemCatalog,
                propertyCatalog));

        return services;
    }
}
