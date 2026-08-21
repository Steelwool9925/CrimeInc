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

        // GET /content/assets/{key} — streams the file the manifest names for a logical key
        // ("logo", "icon"). Anonymous, because the app shows branding before a guest exists.
        //
        // The key is looked up in the manifest and never treated as a path, so the route cannot
        // be steered at an arbitrary file; PackAssets additionally confines the manifest's own
        // declaration to the pack folder. Serving by key is what lets art be swapped as content,
        // with no code change — the CP4 acceptance test.
        app.MapGet("/content/assets/{key}", [AllowAnonymous] (string key, IContentPackProvider provider) =>
            PackAssets.TryResolve(provider.ActivePack, provider.PackRoot, key, out var path, out var contentType)
                ? Results.File(path, contentType)
                : Results.NotFound())
           .WithName("GetContentAsset")
           .WithTags("Content");

        return app;
    }
}
