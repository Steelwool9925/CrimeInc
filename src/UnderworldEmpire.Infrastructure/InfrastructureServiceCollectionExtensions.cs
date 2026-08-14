using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Domain;
using UnderworldEmpire.Infrastructure.Content;

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

        // Content-pack: load the default pack at startup and register as a singleton.
        // The content root is overridable via "Content:Root" for tests and deployments.
        var contentRoot = configuration["Content:Root"];
        var loader = new ContentPackLoader(contentRoot);
        var pack = loader.Load(BuildInfo.DefaultContentPack);
        services.AddSingleton<IContentPackProvider>(new ContentPackProvider(pack));

        return services;
    }
}
