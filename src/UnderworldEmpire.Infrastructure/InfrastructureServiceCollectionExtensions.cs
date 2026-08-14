using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UnderworldEmpire.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Local-dev fallback connection string, matching <c>docker-compose.yml</c>. Real
    /// environments MUST override it via the <c>ConnectionStrings__Default</c> environment
    /// variable (ASP.NET Core binds double-underscore env vars into configuration). No
    /// production secret is ever committed.
    /// </summary>
    public const string LocalDevConnectionString =
        "Host=localhost;Port=5432;Database=underworld;Username=underworld;Password=underworld";

    /// <summary>Registers the DbContext (PostgreSQL via Npgsql) and infrastructure services.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? LocalDevConnectionString;
        services.AddDbContext<GameDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
