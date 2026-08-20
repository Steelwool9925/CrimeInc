using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnderworldEmpire.Infrastructure;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Integration tests for GET /inventory and the buy/equip/unequip endpoints (T3.2) over real HTTP
/// against real Postgres. Requires the container on port 5433.
/// </summary>
public class InventoryEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    /// <summary>Level 1, 75 cash, +4 attack — buyable outright from the starting 100 cash.</summary>
    private const string CheapWeapon = "brass-knuckles";

    /// <summary>Level 1, 120 cash — above the starting cash, so it exercises the cash gate.</summary>
    private const string UnaffordableArmor = "leather-jacket";

    /// <summary>Level 2 — above a new player's level, so it exercises the level gate.</summary>
    private const string GatedWeapon = "switchblade";

    /// <summary>Level 1, 120 cash, +5 defense — an armour piece, for cross-slot aggregation.</summary>
    private const string CheapArmor = "leather-jacket";

    public InventoryEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        factory.MigrateDb();
        _client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string> NewGuestTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/session", new { });
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("deviceToken").GetString()!;
    }

    private async Task<HttpResponseMessage> GetInventoryAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/inventory");
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static JsonElement ItemNamed(JsonElement body, string itemId) =>
        body.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == itemId);

    private async Task<HttpResponseMessage> PostAsync(string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> BuyAsync(string token, string itemId) =>
        PostAsync(token, $"/items/{itemId}/buy");

    private Task<HttpResponseMessage> EquipAsync(string token, string itemId) =>
        PostAsync(token, $"/items/{itemId}/equip");

    private Task<HttpResponseMessage> UnequipAsync(string token, string itemId) =>
        PostAsync(token, $"/items/{itemId}/unequip");

    /// <summary>
    /// Tops the player up directly in the database. The catalog is priced so a fresh player can afford
    /// exactly one cheap item, which is the point of that balance — but a test about wearing two things
    /// at once should not have to grind jobs to get there, and grinding would make it depend on a random
    /// payout range.
    /// </summary>
    private async Task GrantAsync(string deviceToken, long cash = 0, int? level = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GameDbContext>();

        var player = await db.Players.SingleAsync(p => p.DeviceToken == deviceToken);
        player.AddCash(cash);
        if (level is not null)
            player.Level = level.Value;

        await db.SaveChangesAsync();
    }

    private static string? ReasonOf(JsonDocument problem) =>
        problem.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;

    // -------------------------------------------------------------------------
    // GET /inventory
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Inventory_without_authorization_returns_401()
    {
        var response = await _client.GetAsync("/inventory");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Inventory_lists_every_catalog_item_owning_none_of_them()
    {
        var token = await NewGuestTokenAsync();

        var response = await GetInventoryAsync(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        var items = doc.RootElement.GetProperty("items");

        // The whole catalog, not just holdings: the Store and Inventory screens are two views of
        // the same list, so one call serves both.
        Assert.Equal(9, items.GetArrayLength());
        Assert.All(items.EnumerateArray(), i =>
        {
            Assert.Equal(0, i.GetProperty("quantity").GetInt32());
            Assert.False(i.GetProperty("equipped").GetBoolean());
        });
    }

    [Fact]
    public async Task Inventory_carries_the_content_definition_of_each_item()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetInventoryAsync(token));
        var knuckles = ItemNamed(doc.RootElement, CheapWeapon);

        Assert.Equal("Brass Knuckles", knuckles.GetProperty("name").GetString());
        Assert.Equal("weapon", knuckles.GetProperty("slot").GetString());
        Assert.Equal(1, knuckles.GetProperty("levelRequirement").GetInt32());
        Assert.Equal(75, knuckles.GetProperty("price").GetInt64());
        Assert.Equal(4, knuckles.GetProperty("attack").GetInt32());
        Assert.Equal(0, knuckles.GetProperty("defense").GetInt32());
    }

    [Fact]
    public async Task An_affordable_in_level_item_is_reported_as_buyable()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetInventoryAsync(token));
        var knuckles = ItemNamed(doc.RootElement, CheapWeapon);

        Assert.True(knuckles.GetProperty("canBuy").GetBoolean());
        Assert.Equal(JsonValueKind.Null, knuckles.GetProperty("buyUnavailableReason").ValueKind);
    }

    [Fact]
    public async Task An_item_above_the_players_level_reports_the_level_reason()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetInventoryAsync(token));
        var switchblade = ItemNamed(doc.RootElement, GatedWeapon);

        Assert.False(switchblade.GetProperty("canBuy").GetBoolean());
        Assert.Equal("level", switchblade.GetProperty("buyUnavailableReason").GetString());
    }

    [Fact]
    public async Task An_item_the_player_cannot_afford_reports_the_cash_reason()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetInventoryAsync(token));
        var jacket = ItemNamed(doc.RootElement, UnaffordableArmor);

        Assert.False(jacket.GetProperty("canBuy").GetBoolean());
        Assert.Equal("cash", jacket.GetProperty("buyUnavailableReason").GetString());
    }

    [Fact]
    public async Task An_unowned_item_cannot_be_equipped_and_says_so()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetInventoryAsync(token));
        var knuckles = ItemNamed(doc.RootElement, CheapWeapon);

        Assert.False(knuckles.GetProperty("canEquip").GetBoolean());
        Assert.Equal("notOwned", knuckles.GetProperty("equipUnavailableReason").GetString());
    }

    [Fact]
    public async Task A_new_players_loadout_is_their_base_stats_with_nothing_from_gear()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetInventoryAsync(token));
        var loadout = doc.RootElement.GetProperty("loadout");

        Assert.Equal(5, loadout.GetProperty("baseAttack").GetInt32());
        Assert.Equal(0, loadout.GetProperty("itemAttack").GetInt32());
        Assert.Equal(5, loadout.GetProperty("totalAttack").GetInt32());
        Assert.Equal(5, loadout.GetProperty("baseDefense").GetInt32());
        Assert.Equal(0, loadout.GetProperty("itemDefense").GetInt32());
        Assert.Equal(5, loadout.GetProperty("totalDefense").GetInt32());
    }

    [Fact]
    public async Task Inventory_reports_the_cash_and_level_the_gating_was_computed_against()
    {
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await GetInventoryAsync(token));

        // Sent so the client can render affordability without a second call to /me, exactly as
        // GET /jobs sends currentEnergy and level.
        Assert.Equal(100, doc.RootElement.GetProperty("cash").GetInt64());
        Assert.Equal(1, doc.RootElement.GetProperty("level").GetInt32());
    }

    // -------------------------------------------------------------------------
    // POST /items/{id}/buy
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Buying_without_authorization_returns_401()
    {
        var response = await _client.PostAsync($"/items/{CheapWeapon}/buy", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Buying_an_unknown_item_returns_404()
    {
        var token = await NewGuestTokenAsync();

        var response = await BuyAsync(token, "no-such-item");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Item_ids_are_case_sensitive()
    {
        // Ids are content keys. Accepting a different casing would quietly admit a second spelling.
        var token = await NewGuestTokenAsync();

        var response = await BuyAsync(token, CheapWeapon.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Buying_an_item_charges_its_price_and_adds_a_copy()
    {
        var token = await NewGuestTokenAsync();

        var response = await BuyAsync(token, CheapWeapon);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);

        Assert.Equal(CheapWeapon, doc.RootElement.GetProperty("itemId").GetString());
        Assert.Equal(75, doc.RootElement.GetProperty("cashSpent").GetInt64());
        Assert.Equal(1, doc.RootElement.GetProperty("quantityOwned").GetInt32());
        Assert.Equal(25, doc.RootElement.GetProperty("player").GetProperty("cash").GetInt64());
    }

    [Fact]
    public async Task Buying_does_not_equip()
    {
        // A cheap item bought to hold must not silently displace something better.
        var token = await NewGuestTokenAsync();

        using var doc = await BodyAsync(await BuyAsync(token, CheapWeapon));

        Assert.Equal(0, doc.RootElement.GetProperty("loadout").GetProperty("itemAttack").GetInt32());
        Assert.Equal(5, doc.RootElement.GetProperty("loadout").GetProperty("totalAttack").GetInt32());
    }

    [Fact]
    public async Task A_purchase_survives_into_the_next_inventory_read()
    {
        var token = await NewGuestTokenAsync();
        await BuyAsync(token, CheapWeapon);

        using var doc = await BodyAsync(await GetInventoryAsync(token));
        var knuckles = ItemNamed(doc.RootElement, CheapWeapon);

        Assert.Equal(1, knuckles.GetProperty("quantity").GetInt32());
        Assert.True(knuckles.GetProperty("canEquip").GetBoolean());
        Assert.Equal(25, doc.RootElement.GetProperty("cash").GetInt64());
    }

    [Fact]
    public async Task Buying_something_unaffordable_is_refused_with_the_cash_reason()
    {
        var token = await NewGuestTokenAsync();

        var response = await BuyAsync(token, UnaffordableArmor);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = await BodyAsync(response);
        Assert.Equal("cash", ReasonOf(problem));
    }

    [Fact]
    public async Task A_refused_purchase_charges_nothing()
    {
        var token = await NewGuestTokenAsync();

        await BuyAsync(token, UnaffordableArmor);

        using var doc = await BodyAsync(await GetInventoryAsync(token));
        Assert.Equal(100, doc.RootElement.GetProperty("cash").GetInt64());
        Assert.Equal(0, ItemNamed(doc.RootElement, UnaffordableArmor).GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Buying_above_your_level_is_refused_with_the_level_reason()
    {
        var token = await NewGuestTokenAsync();
        await GrantAsync(token, cash: 10_000);

        var response = await BuyAsync(token, GatedWeapon);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = await BodyAsync(response);
        Assert.Equal("level", ReasonOf(problem));
    }

    [Fact]
    public async Task A_second_copy_raises_the_quantity_rather_than_adding_a_row()
    {
        var token = await NewGuestTokenAsync();
        await GrantAsync(token, cash: 75);

        await BuyAsync(token, CheapWeapon);
        using var doc = await BodyAsync(await BuyAsync(token, CheapWeapon));

        Assert.Equal(2, doc.RootElement.GetProperty("quantityOwned").GetInt32());
        Assert.Equal(25, doc.RootElement.GetProperty("player").GetProperty("cash").GetInt64());
    }

    // -------------------------------------------------------------------------
    // POST /items/{id}/equip — CP3's first line
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Equipping_a_weapon_raises_effective_attack()
    {
        var token = await NewGuestTokenAsync();
        await BuyAsync(token, CheapWeapon);

        var response = await EquipAsync(token, CheapWeapon);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        var loadout = doc.RootElement.GetProperty("loadout");

        Assert.Equal(CheapWeapon, doc.RootElement.GetProperty("equippedItemId").GetString());
        Assert.Equal(5, loadout.GetProperty("baseAttack").GetInt32());
        Assert.Equal(4, loadout.GetProperty("itemAttack").GetInt32());
        Assert.Equal(9, loadout.GetProperty("totalAttack").GetInt32());
    }

    [Fact]
    public async Task Equipping_leaves_the_base_stat_alone()
    {
        // Gear is a bonus computed on read, never written onto the player: a retune of items.json has
        // to move every player's numbers, which a stored effective stat would silently outlive.
        var token = await NewGuestTokenAsync();
        await BuyAsync(token, CheapWeapon);

        using var doc = await BodyAsync(await EquipAsync(token, CheapWeapon));

        Assert.Equal(5, doc.RootElement.GetProperty("player").GetProperty("attack").GetInt32());
    }

    [Fact]
    public async Task Equipping_something_you_do_not_own_is_refused()
    {
        var token = await NewGuestTokenAsync();

        var response = await EquipAsync(token, CheapWeapon);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = await BodyAsync(response);
        Assert.Equal("notOwned", ReasonOf(problem));
    }

    [Fact]
    public async Task Equipping_something_already_worn_is_refused()
    {
        var token = await NewGuestTokenAsync();
        await BuyAsync(token, CheapWeapon);
        await EquipAsync(token, CheapWeapon);

        var response = await EquipAsync(token, CheapWeapon);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = await BodyAsync(response);
        Assert.Equal("alreadyEquipped", ReasonOf(problem));
    }

    [Fact]
    public async Task Gear_in_different_slots_stacks()
    {
        var token = await NewGuestTokenAsync();
        await GrantAsync(token, cash: 200);

        await BuyAsync(token, CheapWeapon);
        await BuyAsync(token, CheapArmor);
        await EquipAsync(token, CheapWeapon);
        using var doc = await BodyAsync(await EquipAsync(token, CheapArmor));

        var loadout = doc.RootElement.GetProperty("loadout");
        Assert.Equal(9, loadout.GetProperty("totalAttack").GetInt32());   // 5 base + 4 knuckles
        Assert.Equal(10, loadout.GetProperty("totalDefense").GetInt32()); // 5 base + 5 jacket
    }

    [Fact]
    public async Task Equipping_a_second_item_in_a_slot_displaces_the_first()
    {
        // The invariant with no database constraint behind it: two weapons worn at once would hand the
        // player both bonuses.
        var token = await NewGuestTokenAsync();
        await GrantAsync(token, cash: 500, level: 2);

        await BuyAsync(token, CheapWeapon);
        await BuyAsync(token, GatedWeapon);
        await EquipAsync(token, CheapWeapon);

        using var doc = await BodyAsync(await EquipAsync(token, GatedWeapon));

        Assert.Equal(GatedWeapon, doc.RootElement.GetProperty("equippedItemId").GetString());
        Assert.Equal(CheapWeapon, doc.RootElement.GetProperty("unequippedItemId").GetString());

        // 7 attack from the switchblade only — never 11, which is what double-counting would give.
        Assert.Equal(7, doc.RootElement.GetProperty("loadout").GetProperty("itemAttack").GetInt32());
    }

    [Fact]
    public async Task The_displaced_item_is_reported_as_unworn_by_the_next_inventory_read()
    {
        var token = await NewGuestTokenAsync();
        await GrantAsync(token, cash: 500, level: 2);

        await BuyAsync(token, CheapWeapon);
        await BuyAsync(token, GatedWeapon);
        await EquipAsync(token, CheapWeapon);
        await EquipAsync(token, GatedWeapon);

        using var doc = await BodyAsync(await GetInventoryAsync(token));

        Assert.False(ItemNamed(doc.RootElement, CheapWeapon).GetProperty("equipped").GetBoolean());
        Assert.True(ItemNamed(doc.RootElement, GatedWeapon).GetProperty("equipped").GetBoolean());
    }

    // -------------------------------------------------------------------------
    // POST /items/{id}/unequip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Unequipping_removes_the_bonus_and_keeps_the_item()
    {
        var token = await NewGuestTokenAsync();
        await BuyAsync(token, CheapWeapon);
        await EquipAsync(token, CheapWeapon);

        var response = await UnequipAsync(token, CheapWeapon);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        Assert.Equal(CheapWeapon, doc.RootElement.GetProperty("unequippedItemId").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("equippedItemId").ValueKind);
        Assert.Equal(5, doc.RootElement.GetProperty("loadout").GetProperty("totalAttack").GetInt32());

        using var inventory = await BodyAsync(await GetInventoryAsync(token));
        Assert.Equal(1, ItemNamed(inventory.RootElement, CheapWeapon).GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Unequipping_something_you_are_not_wearing_is_refused()
    {
        var token = await NewGuestTokenAsync();
        await BuyAsync(token, CheapWeapon);

        var response = await UnequipAsync(token, CheapWeapon);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = await BodyAsync(response);
        Assert.Equal("notEquipped", ReasonOf(problem));
    }

    [Fact]
    public async Task Unequipping_an_unknown_item_returns_404()
    {
        var token = await NewGuestTokenAsync();

        var response = await UnequipAsync(token, "no-such-item");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
