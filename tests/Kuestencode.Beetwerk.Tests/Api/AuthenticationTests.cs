using System.Net;
using System.Net.Http.Json;
using Kuestencode.Beetwerk.Api.Auth;

namespace Kuestencode.Beetwerk.Tests.Api;

public class AuthenticationTests : IDisposable
{
    private readonly BeetwerkFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await factory.CreateBrowser().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_page_is_anonymous()
    {
        var response = await factory.CreateBrowser().GetAsync("/login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<form method=\"post\" action=\"/login\">", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/config")]
    [InlineData("/api/objects")]
    [InlineData("/api/tasks")]
    [InlineData("/api/does-not-exist")]
    public async Task Api_requires_login(string path)
    {
        var response = await factory.CreateBrowser().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Push_subscription_endpoint_requires_login()
    {
        var response = await factory.CreateBrowser().PostAsJsonAsync("/api/push/subscriptions",
            new { endpoint = "https://push.example/1", keys = new { p256dh = "a", auth = "b" } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/aufgaben/3")]
    [InlineData("/sw.js")]
    [InlineData("/manifest.webmanifest")]
    [InlineData("/assets/index.js")]
    public async Task Ui_and_static_files_redirect_to_login(string path)
    {
        var response = await factory.CreateBrowser().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login?returnUrl=", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Valid_login_grants_access()
    {
        var client = await factory.CreateLoggedInClientAsync();
        var config = await (await client.GetAsync("/api/config")).ReadAsync<Dictionary<string, object>>();
        Assert.Equal("admin", config["username"]?.ToString());
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var client = factory.CreateBrowser();
        var response = await client.PostAsync("/login", BeetwerkFactory.LoginForm("admin", "falsch"));
        Assert.Contains("error=invalid", response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/config")).StatusCode);
    }

    [Fact]
    public async Task Username_is_locked_after_repeated_failures()
    {
        var client = factory.CreateBrowser();
        for (var i = 0; i < LoginThrottle.MaxFailures; i++)
            await client.PostAsync("/login", BeetwerkFactory.LoginForm("admin", "falsch"));

        var response = await client.PostAsync("/login", BeetwerkFactory.LoginForm("admin", BeetwerkFactory.AdminPassword));
        Assert.Contains("error=locked", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Open_redirect_is_prevented()
    {
        var response = await factory.CreateBrowser().PostAsync("/login",
            BeetwerkFactory.LoginForm("admin", BeetwerkFactory.AdminPassword, "//evil.example/"));
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Logout_ends_session()
    {
        var client = await factory.CreateLoggedInClientAsync();
        await client.PostAsync("/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/config")).StatusCode);
    }

    [Theory]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("//evil", "/")]
    [InlineData("/\\evil", "/")]
    [InlineData("https://evil", "/")]
    [InlineData(null, "/")]
    public void SafeReturnUrl_only_allows_local_paths(string? input, string expected)
    {
        Assert.Equal(expected, LoginEndpoints.SafeReturnUrl(input));
    }
}

public class NoAuthModeTests
{
    [Fact]
    public async Task Api_is_reachable_without_login()
    {
        using var factory = new BeetwerkFactory(new() { ["AUTH_MODE"] = "none" });
        var client = factory.CreateBrowser();
        var config = await (await client.GetAsync("/api/config")).ReadAsync<Dictionary<string, object>>();
        Assert.Equal("none", config["authMode"]?.ToString());
        Assert.Equal(UserService.ImplicitUsername, config["username"]?.ToString());
    }

    [Fact]
    public void Refuses_to_start_with_public_url()
    {
        using var factory = new BeetwerkFactory(new() { ["AUTH_MODE"] = "none", ["PUBLIC_URL"] = "https://garten.example" });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("AUTH_ALLOW_NONE_PUBLIC", error.ToString());
    }

    [Fact]
    public async Task Starts_with_public_url_when_explicitly_allowed()
    {
        using var factory = new BeetwerkFactory(new()
        {
            ["AUTH_MODE"] = "none", ["PUBLIC_URL"] = "https://garten.example", ["AUTH_ALLOW_NONE_PUBLIC"] = "true"
        });
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/api/config")).StatusCode);
    }
}
