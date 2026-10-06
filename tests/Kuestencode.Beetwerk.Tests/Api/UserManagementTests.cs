using System.Net;
using System.Net.Http.Json;
using Kuestencode.Beetwerk.Api.Auth;
using Kuestencode.Beetwerk.Api.Contracts;

namespace Kuestencode.Beetwerk.Tests.Api;

public class UserManagementTests : IAsyncLifetime
{
    private readonly BeetwerkFactory factory = new();
    private HttpClient admin = null!;

    public async Task InitializeAsync() => admin = await factory.CreateLoggedInClientAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<HttpClient> LoginAs(string username, string password)
    {
        var client = factory.CreateBrowser();
        var response = await client.PostAsync("/login", BeetwerkFactory.LoginForm(username, password));
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        return client;
    }

    [Fact]
    public async Task Users_endpoint_requires_login()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateBrowser().GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Created_user_can_log_in_and_is_listed()
    {
        var created = await admin.PostAsJsonAsync("/api/users", new CreateUserInput("anna", "anna-passwort"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var anna = await LoginAs("anna", "anna-passwort");
        var users = await (await anna.GetAsync("/api/users")).ReadAsync<List<UserDto>>();
        Assert.Equal(["admin", "anna"], users.Select(u => u.Username));
        Assert.True(users.Single(u => u.Username == "anna").IsCurrent);
    }

    [Theory]
    [InlineData("anna", "kurz", HttpStatusCode.BadRequest)]
    [InlineData("an na", "lang-genug", HttpStatusCode.BadRequest)]
    [InlineData("garten", "lang-genug", HttpStatusCode.BadRequest)]
    [InlineData("ADMIN", "lang-genug", HttpStatusCode.Conflict)]
    public async Task Invalid_users_are_rejected(string username, string password, HttpStatusCode expected)
    {
        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserInput(username, password));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Deleted_user_loses_access_immediately()
    {
        await admin.PostAsJsonAsync("/api/users", new CreateUserInput("ben", "ben-passwort"));
        var ben = await LoginAs("ben", "ben-passwort");
        Assert.Equal(HttpStatusCode.OK, (await ben.GetAsync("/api/config")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/users/ben")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ben.GetAsync("/api/config")).StatusCode);
    }

    [Fact]
    public async Task Password_change_ends_other_sessions_but_keeps_own()
    {
        await admin.PostAsJsonAsync("/api/users", new CreateUserInput("clara", "clara-alt-123"));
        var phone = await LoginAs("clara", "clara-alt-123");
        var laptop = await LoginAs("clara", "clara-alt-123");

        var change = await laptop.PutAsJsonAsync("/api/users/clara/password", new PasswordInput("clara-neu-456"));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/config")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/config")).StatusCode);
        await LoginAs("clara", "clara-neu-456");
    }

    [Fact]
    public async Task Setting_another_users_password_logs_that_user_out()
    {
        await admin.PostAsJsonAsync("/api/users", new CreateUserInput("dora", "dora-passwort"));
        var dora = await LoginAs("dora", "dora-passwort");

        await admin.PutAsJsonAsync("/api/users/dora/password", new PasswordInput("vergessen-123"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await dora.GetAsync("/api/config")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/config")).StatusCode);
    }

    [Fact]
    public async Task Password_rules_and_unknown_users()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/users/admin/password", new PasswordInput("kurz"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/users/niemand/password", new PasswordInput("lang-genug"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/users/niemand")).StatusCode);
    }

    [Fact]
    public async Task Cannot_delete_yourself()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/api/users/admin")).StatusCode);
    }

    [Fact]
    public void Username_validation()
    {
        Assert.Equal("anna-maria.k_1", UserService.ValidateUsername("  anna-maria.k_1 "));
        Assert.Equal("Jürgen", UserService.ValidateUsername("Jürgen"));
        Assert.Throws<ArgumentException>(() => UserService.ValidateUsername(""));
        Assert.Throws<ArgumentException>(() => UserService.ValidateUsername("a/b"));
    }
}

public class LastUserTests
{
    [Fact]
    public async Task Last_user_cannot_be_deleted()
    {
        using var factory = new BeetwerkFactory(new() { ["AUTH_MODE"] = "none" });
        var client = factory.CreateBrowser();
        await client.PostAsJsonAsync("/api/users", new CreateUserInput("einzig", "einzig-passwort"));
        // Nur ein Nutzer mit Passwort (admin wird im Modus none nicht angelegt) – der bleibt.
        var users = await (await client.GetAsync("/api/users")).ReadAsync<List<UserDto>>();
        Assert.Equal("einzig", Assert.Single(users).Username);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/users/einzig")).StatusCode);
    }
}
