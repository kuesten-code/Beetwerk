using System.Collections.Concurrent;
using System.Net.Http.Json;
using Kuestencode.Beetwerk.Api.Push;
using Kuestencode.Beetwerk.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Kuestencode.Beetwerk.Tests.Api;

public sealed class FakePushSender : IPushSender
{
    public ConcurrentQueue<(string Endpoint, PushMessage Message)> Sent { get; } = new();
    public HashSet<string> ExpiredEndpoints { get; } = [];

    public Task<PushDeliveryResult> SendAsync(PushSubscription subscription, PushMessage message, CancellationToken ct)
    {
        if (ExpiredEndpoints.Contains(subscription.Endpoint))
            return Task.FromResult(PushDeliveryResult.Expired);
        Sent.Enqueue((subscription.Endpoint, message));
        return Task.FromResult(PushDeliveryResult.Delivered);
    }
}

/// <summary>Startet die echte App mit eigener SQLite-Datei in einem Temp-Verzeichnis.</summary>
public sealed class BeetwerkFactory(Dictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "geheim-genug-123";

    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "beetwerk-tests", Guid.NewGuid().ToString("N"));

    public FakePushSender Push { get; } = new();

    // Mittwoch, 10 Uhr UTC – liegt in jeder Zeitzone Mitteleuropas nach NOTIFY_HOUR=0.
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        var defaults = new Dictionary<string, string?>
        {
            ["DATA_DIR"] = dataDirectory,
            ["AUTH_MODE"] = "login",
            ["ADMIN_USERNAME"] = AdminUser,
            ["ADMIN_PASSWORD"] = AdminPassword,
            ["PUSH_ENABLED"] = "false",
            ["NOTIFY_HOUR"] = "0",
            ["TRUST_FORWARDED_HEADERS"] = "false"
        };
        foreach (var (key, value) in settings ?? [])
            defaults[key] = value;
        foreach (var (key, value) in defaults)
            builder.UseSetting(key, value);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender>(Push);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            // Tests rufen den TaskNotifier gezielt auf; ein parallel laufender Scheduler würde sie unvorhersehbar machen.
            var scheduler = services.SingleOrDefault(d => d.ImplementationType == typeof(TaskNotificationService));
            if (scheduler is not null)
                services.Remove(scheduler);
        });
    }

    public HttpClient CreateBrowser() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<HttpClient> CreateLoggedInClientAsync()
    {
        var client = CreateBrowser();
        var response = await client.PostAsync("/login", LoginForm(AdminUser, AdminPassword));
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        return client;
    }

    public static FormUrlEncodedContent LoginForm(string username, string password, string returnUrl = "/") =>
        new(new Dictionary<string, string> { ["username"] = username, ["password"] = password, ["returnUrl"] = returnUrl });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Temp-Verzeichnis; wenn Windows die Datei noch hält, räumt das System später auf.
        }
    }
}

public static class HttpClientExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            Assert.Fail($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options))!;
    }
}

public static class TestJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
