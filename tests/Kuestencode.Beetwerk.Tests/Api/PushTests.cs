using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Api.Push;
using Kuestencode.Beetwerk.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kuestencode.Beetwerk.Tests.Api;

public class PushTests : IAsyncLifetime
{
    private readonly BeetwerkFactory factory = new(new() { ["PUSH_ENABLED"] = "true" });
    private HttpClient client = null!;

    private static readonly JsonElement Point = JsonDocument.Parse("""{"type":"Point","coordinates":[10.13,54.32]}""").RootElement.Clone();
    private static readonly DateOnly Today = new(2026, 10, 7);

    public async Task InitializeAsync() => client = await factory.CreateLoggedInClientAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    private Task Subscribe(string endpoint) =>
        client.PostAsJsonAsync("/api/push/subscriptions", new PushSubscriptionInput(endpoint, new PushSubscriptionKeys("p256dh", "auth"), "Test"));

    private async Task<GardenTaskDto> CreateTask(string title, DateOnly due, bool notify = true, int leadDays = 0) =>
        await (await client.PostAsJsonAsync("/api/tasks",
            new GardenTaskInput(title, null, null, Point, due, notify, leadDays, RecurrenceFrequency.None, 1, null, null))).ReadAsync<GardenTaskDto>();

    private async Task<int> RunNotifier()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TaskNotifier>().RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Config_exposes_vapid_key_when_push_is_enabled()
    {
        var config = await (await client.GetAsync("/api/config")).ReadAsync<JsonElement>();
        Assert.True(config.GetProperty("pushEnabled").GetBoolean());
        Assert.False(string.IsNullOrEmpty(config.GetProperty("vapidPublicKey").GetString()));
    }

    [Fact]
    public async Task Subscription_requires_https_endpoint()
    {
        var response = await client.PostAsJsonAsync("/api/push/subscriptions",
            new PushSubscriptionInput("http://push.example/1", new PushSubscriptionKeys("a", "b"), null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Due_tasks_are_notified_exactly_once()
    {
        await Subscribe("https://push.example/phone");
        await CreateTask("Heute", Today);
        await CreateTask("Mit Vorlauf", Today.AddDays(3), leadDays: 3);
        await CreateTask("Später", Today.AddDays(10), leadDays: 2);
        await CreateTask("Ohne Push", Today, notify: false);

        Assert.Equal(2, await RunNotifier());
        Assert.Equal(0, await RunNotifier());

        var titles = factory.Push.Sent.Select(s => s.Message.Title).Order().ToList();
        Assert.Equal(["Heute", "Mit Vorlauf"], titles);
        Assert.All(factory.Push.Sent, s => Assert.StartsWith("/aufgaben/", s.Message.Url));
    }

    [Fact]
    public async Task Many_due_tasks_are_summarized()
    {
        await Subscribe("https://push.example/phone");
        for (var i = 0; i <= TaskNotifier.SummaryThreshold; i++)
            await CreateTask($"Aufgabe {i}", Today);

        await RunNotifier();

        var message = Assert.Single(factory.Push.Sent).Message;
        Assert.Equal("/aufgaben", message.Url);
    }

    [Fact]
    public async Task Expired_subscriptions_are_removed()
    {
        await Subscribe("https://push.example/gone");
        factory.Push.ExpiredEndpoints.Add("https://push.example/gone");

        var result = await (await client.PostAsync("/api/push/test", null)).ReadAsync<JsonElement>();
        Assert.Equal(0, result.GetProperty("delivered").GetInt32());

        factory.Push.ExpiredEndpoints.Clear();
        var again = await (await client.PostAsync("/api/push/test", null)).ReadAsync<JsonElement>();
        Assert.Equal(0, again.GetProperty("delivered").GetInt32());
    }

    [Fact]
    public async Task Unsubscribe_removes_subscription()
    {
        await Subscribe("https://push.example/tablet");
        await client.DeleteAsync("/api/push/subscriptions?endpoint=" + Uri.EscapeDataString("https://push.example/tablet"));
        var result = await (await client.PostAsync("/api/push/test", null)).ReadAsync<JsonElement>();
        Assert.Equal(0, result.GetProperty("delivered").GetInt32());
    }

    [Theory]
    [InlineData(-2, "Überfällig seit 05.10.")]
    [InlineData(0, "Heute fällig")]
    [InlineData(1, "Morgen fällig")]
    [InlineData(4, "Fällig in 4 Tagen (11.10.)")]
    public void DueText_describes_relative_due_date(int offset, string expected)
    {
        Assert.Equal(expected, TaskNotifier.DueText(Today.AddDays(offset), Today));
    }
}

public class OptionsTests
{
    private static BeetwerkOptions Parse(Dictionary<string, string?> values) =>
        BeetwerkOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), Path.GetTempPath());

    [Fact]
    public void Defaults_are_login_with_push_and_sh_orthophotos()
    {
        var options = Parse([]);
        Assert.Equal(AuthMode.Login, options.AuthMode);
        Assert.True(options.PushEnabled);
        Assert.Contains("sh_dop20_rgb", options.PrimaryTiles.Url);
        Assert.NotNull(options.FallbackTiles);
    }

    [Theory]
    [InlineData("http://garten.example", false)]
    [InlineData("https://garten.example", true)]
    [InlineData("http://localhost:8080", true)]
    public void Push_is_disabled_for_insecure_public_url(string url, bool enabled)
    {
        Assert.Equal(enabled, Parse(new() { ["PUBLIC_URL"] = url }).PushEnabled);
    }

    [Fact]
    public void Version_follows_werkbank_fallback_chain()
    {
        Assert.Equal("dev", Parse([]).Version);
        Assert.Equal("0.2.0", Parse(new() { ["DOCKER_IMAGE_TAG"] = "0.2.0" }).Version);
        Assert.Equal("1.0.0", Parse(new() { ["DOCKER_IMAGE_TAG"] = "0.2.0", ["MODULE_VERSION"] = "1.0.0" }).Version);
    }

    [Fact]
    public void Push_can_be_switched_off()
    {
        Assert.False(Parse(new() { ["PUSH_ENABLED"] = "false" }).PushEnabled);
    }

    [Fact]
    public void Unknown_auth_mode_fails_fast()
    {
        Assert.Throws<InvalidOperationException>(() => Parse(new() { ["AUTH_MODE"] = "basic" }));
    }

    [Fact]
    public void Fallback_layer_can_be_disabled()
    {
        Assert.Null(Parse(new() { ["MAP_FALLBACK_URL"] = "none" }).FallbackTiles);
    }

    [Fact]
    public void Notify_hour_is_validated()
    {
        Assert.Equal(8, Parse(new() { ["NOTIFY_HOUR"] = "99" }).NotifyHour);
        Assert.Equal(6, Parse(new() { ["NOTIFY_HOUR"] = "6" }).NotifyHour);
    }
}
