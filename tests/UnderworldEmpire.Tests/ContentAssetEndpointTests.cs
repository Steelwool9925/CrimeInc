using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Covers <c>GET /content/assets/{key}</c> (T4.1) against the real shipped <c>crime</c> pack.
///
/// The route is anonymous — the app needs the logo before it has a session — and serves whatever
/// file the manifest names for the key. These tests hit the actual pack rather than a fixture, so
/// they also prove the asset files reach the build output, which the JSON-only content glob in
/// the API csproj did not previously do.
/// </summary>
public class ContentAssetEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly WebApplicationFactory<Program> _factory;

    public ContentAssetEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("logo")]
    [InlineData("icon")]
    public async Task Declared_assets_are_served_as_png(string key)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/content/assets/{key}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0, "asset body was empty");
        Assert.Equal(PngSignature, bytes.Take(PngSignature.Length));
    }

    [Fact]
    public async Task The_asset_route_needs_no_session()
    {
        // No device token is attached: the splash needs the logo before a guest exists.
        var response = await _factory.CreateClient().GetAsync("/content/assets/logo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_undeclared_key_is_404()
    {
        var response = await _factory.CreateClient().GetAsync("/content/assets/banner");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_traversal_attempt_in_the_url_is_not_served()
    {
        // The key is looked up in the manifest, never used as a path, so this can only 404 or 400.
        var response = await _factory.CreateClient().GetAsync("/content/assets/..%2F..%2Fappsettings.json");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_manifest_advertises_the_keys_the_route_serves()
    {
        // Ties the two halves together: whatever GET /content/pack advertises under "assets"
        // must actually be fetchable, or the client would render a broken image.
        var client = _factory.CreateClient();
        using var pack = await client.GetStreamAsync("/content/pack");
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(pack);

        var assets = doc.RootElement.GetProperty("assets");

        foreach (var declared in assets.EnumerateObject())
        {
            var response = await client.GetAsync($"/content/assets/{declared.Name}");
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"manifest advertises asset '{declared.Name}' but the route returned {(int)response.StatusCode}");
        }
    }
}
