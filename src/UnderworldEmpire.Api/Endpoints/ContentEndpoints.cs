using Microsoft.AspNetCore.Authorization;
using UnderworldEmpire.Infrastructure.Content;

namespace UnderworldEmpire.Api.Endpoints;

/// <summary>Minimal-API endpoints that expose content-pack metadata (T1.4).</summary>
public static class ContentEndpoints
{
    /// <summary>Registers the content endpoints on the provided <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /content/pack — returns the active pack manifest; anonymous (no auth required).
        app.MapGet("/content/pack", [AllowAnonymous] (IContentPackProvider provider) =>
            Results.Ok(provider.ActivePack))
           .WithName("GetContentPack")
           .WithTags("Content");

        return app;
    }
}
