using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace UnderworldEmpire.Tests;

/// <summary>
/// Integration tests for POST /account/register — claiming a device's guest player with a
/// username and password. Requires the Postgres container on port 5433 with the
/// <c>underworld_test</c> database.
/// </summary>
public class AccountEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AccountEndpointTests(CustomWebApplicationFactory factory)
    {
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

    private async Task<HttpResponseMessage> RegisterAsync(string? token, string username, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/register")
        {
            Content = JsonContent.Create(new { username, password })
        };
        if (token is not null)
            request.Headers.Add("Authorization", $"Bearer {token}");
        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private async Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        await _client.PostAsJsonAsync("/account/login", new { username, password });

    /// <summary>
    /// A fresh, mixed-case, valid username (e.g. "Al3f9c2b1a7d"). The Postgres container behind
    /// these tests is not reset between test methods or test runs, so a literal like
    /// "AliceCarter" would collide with itself the moment the suite runs twice; each call here
    /// mints a value unique enough to never collide while still starting mixed-case ("Al" +
    /// lowercase hex), exactly like the "Al" + enough-characters example in the brief.
    /// </summary>
    private static string UniqueUsername() => "Al" + Guid.NewGuid().ToString("N")[..10];

    // -------------------------------------------------------------------------
    // POST /account/register — happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Valid_registration_returns_200_with_lowercased_username()
    {
        var token = await NewGuestTokenAsync();
        var username = UniqueUsername();

        var response = await RegisterAsync(token, username, "correct-horse-battery");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await BodyAsync(response);
        Assert.Equal(username.ToLowerInvariant(), doc.RootElement.GetProperty("username").GetString());
    }

    // -------------------------------------------------------------------------
    // POST /account/register — already claimed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Registering_twice_on_the_same_token_returns_409_alreadyClaimed()
    {
        var token = await NewGuestTokenAsync();

        var first = await RegisterAsync(token, UniqueUsername(), "correct-horse-battery");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await RegisterAsync(token, UniqueUsername(), "correct-horse-battery");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var doc = await BodyAsync(second);
        Assert.Equal("alreadyClaimed", doc.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------------------
    // POST /account/register — unauthenticated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Registering_without_a_bearer_token_returns_401()
    {
        var response = await RegisterAsync(null, "AliceCarter", "correct-horse-battery");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // POST /account/register — invalid username
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Too_short_username_returns_409_usernameInvalid()
    {
        var token = await NewGuestTokenAsync();

        var response = await RegisterAsync(token, "ab", "correct-horse-battery");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var doc = await BodyAsync(response);
        Assert.Equal("usernameInvalid", doc.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------------------
    // POST /account/register — weak password
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Weak_password_returns_409_passwordTooWeak()
    {
        var token = await NewGuestTokenAsync();

        var response = await RegisterAsync(token, UniqueUsername(), "short");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var doc = await BodyAsync(response);
        Assert.Equal("passwordTooWeak", doc.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------------------
    // POST /account/register — username already taken by a different player
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Username_taken_by_a_different_player_returns_409_usernameTaken()
    {
        var firstToken = await NewGuestTokenAsync();
        var secondToken = await NewGuestTokenAsync();
        var username = UniqueUsername();

        var first = await RegisterAsync(firstToken, username, "correct-horse-battery");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await RegisterAsync(secondToken, username, "another-strong-password");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var doc = await BodyAsync(second);
        Assert.Equal("usernameTaken", doc.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------------------
    // POST /account/login — same device token, on a different "device"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Login_with_correct_credentials_returns_the_same_device_token_that_registered()
    {
        var registeringToken = await NewGuestTokenAsync();
        var username = UniqueUsername();
        var password = "correct-horse-battery";

        var register = await RegisterAsync(registeringToken, username, password);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        // A fresh "device" — no bearer token at all — logging in with the account's credentials.
        var login = await LoginAsync(username, password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var doc = await BodyAsync(login);
        var returnedToken = doc.RootElement.GetProperty("deviceToken").GetString();

        // Login never rotates the token — it returns the exact same one every other device
        // logged into this account already holds.
        Assert.Equal(registeringToken, returnedToken);
    }

    // -------------------------------------------------------------------------
    // POST /account/login — refusals are indistinguishable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Login_with_wrong_password_returns_409_invalidCredentials()
    {
        var token = await NewGuestTokenAsync();
        var username = UniqueUsername();
        await RegisterAsync(token, username, "correct-horse-battery");

        var response = await LoginAsync(username, "wrong-password-entirely");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var doc = await BodyAsync(response);
        Assert.Equal("invalidCredentials", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Login_with_an_unknown_username_returns_the_identical_409_invalidCredentials()
    {
        var response = await LoginAsync("nobody-has-ever-registered-this-one", "whatever-password");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var doc = await BodyAsync(response);
        Assert.Equal("invalidCredentials", doc.RootElement.GetProperty("reason").GetString());
    }

    // -------------------------------------------------------------------------
    // POST /account/login — rate limiting
    // -------------------------------------------------------------------------

    [Fact]
    public async Task A_sixth_login_attempt_within_the_window_is_rate_limited()
    {
        // The "login" policy is a fixed window of 5 attempts per 5 minutes, keyed per client IP.
        // This test needs its OWN factory/client rather than the class fixture shared with every
        // other test above: all requests from one in-memory TestServer client share one IP, so
        // this class's other login-calling tests would otherwise eat into the same 5-attempt
        // window and make this test's outcome depend on test execution order.
        using var factory = new CustomWebApplicationFactory();
        factory.MigrateDb();
        using var client = factory.CreateClient();

        async Task<HttpResponseMessage> Attempt(string suffix) =>
            await client.PostAsJsonAsync(
                "/account/login",
                new { username = $"rate-limit-probe-{suffix}", password = "irrelevant-password" });

        for (int i = 0; i < 5; i++)
        {
            var response = await Attempt(i.ToString());
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        var sixth = await Attempt("6th");

        Assert.Equal((HttpStatusCode)429, sixth.StatusCode);
    }
}
