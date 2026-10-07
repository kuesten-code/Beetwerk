using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Api.Devices;
using Kuestencode.Beetwerk.Domain.Devices;
using Kuestencode.Beetwerk.Tests.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Kuestencode.Beetwerk.Tests.Devices;

public class DeviceApiTests : IAsyncLifetime
{
    private readonly BeetwerkFactory factory = new(new() { ["PUSH_ENABLED"] = "true" });
    private HttpClient client = null!;
    private int objectId;

    private static readonly JsonElement Point = JsonDocument.Parse("""{"type":"Point","coordinates":[8.91,54.47]}""").RootElement.Clone();

    public async Task InitializeAsync()
    {
        client = await factory.CreateLoggedInClientAsync();
        var types = await (await client.GetAsync("/api/object-types")).ReadAsync<List<ObjectTypeDto>>();
        var device = types.Single(t => t.Name == "Gerät");
        objectId = (await (await client.PostAsJsonAsync("/api/objects", new GardenObjectInput("Mähroboter", device.Id, Point, null, null, null, null)))
            .ReadAsync<GardenObjectDto>()).Id;
        await client.PostAsJsonAsync("/api/push/subscriptions", new PushSubscriptionInput("https://push.example/1", new PushSubscriptionKeys("a", "b"), null));
    }

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    private Task<HttpResponseMessage> Link(bool createTasks = true) =>
        client.PutAsJsonAsync($"/api/objects/{objectId}/device", new DeviceLinkInput("fake", "mower-1", new() { ["x"] = "y" }, createTasks), TestJson.Options);

    private async Task<int> RefreshAsTheMonitorWould()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Kuestencode.Beetwerk.Data.BeetwerkDbContext>();
        var devices = scope.ServiceProvider.GetRequiredService<DeviceService>();
        var link = db.DeviceLinks.Single();
        factory.Services.GetRequiredService<DeviceStatusCache>().Invalidate(link.Id);
        await devices.RefreshAsync(link, force: true, CancellationToken.None);
        return link.ErrorTaskId ?? 0;
    }

    [Fact]
    public async Task Providers_are_listed_with_configuration_state()
    {
        var providers = await (await client.GetAsync("/api/devices/providers")).ReadAsync<List<DeviceProviderDto>>();
        Assert.Contains(providers, p => p.Key == "husqvarna" && !p.Configured && p.StartNeedsDuration);
        Assert.Contains(providers, p => p.Key == "homeassistant" && !p.Configured);
        Assert.Contains(providers, p => p.Key == "fake" && p.Configured);

        var devices = await (await client.GetAsync("/api/devices/providers/fake/devices")).ReadAsync<List<DeviceInfo>>();
        Assert.Equal("Robi", Assert.Single(devices).Name);
        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/api/devices/providers/husqvarna/devices")).StatusCode);
    }

    [Fact]
    public async Task Linking_returns_status_and_lists_device()
    {
        var link = await (await Link()).ReadAsync<DeviceLinkDto>();
        Assert.Equal(DeviceActivity.Charging, link.Status!.Activity);
        Assert.Equal(90, link.Status.BatteryPercent);

        var all = await (await client.GetAsync("/api/devices")).ReadAsync<List<DeviceLinkDto>>();
        Assert.Equal(objectId, Assert.Single(all).ObjectId);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/objects/{objectId}/device")).StatusCode);
        Assert.Empty(await (await client.GetAsync("/api/devices")).ReadAsync<List<DeviceLinkDto>>());
    }

    [Fact]
    public async Task Unknown_provider_or_object_is_rejected()
    {
        var unknown = await client.PutAsJsonAsync($"/api/objects/{objectId}/device", new DeviceLinkInput("gibtsnicht", "x", null, true), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var missing = await client.PutAsJsonAsync("/api/objects/9999/device", new DeviceLinkInput("fake", "mower-1", null, true), TestJson.Options);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Commands_are_forwarded_and_validated()
    {
        await Link();

        var start = await client.PostAsJsonAsync($"/api/objects/{objectId}/device/commands", new DeviceCommandInput(DeviceCommand.Start, 120), TestJson.Options);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(("mower-1", DeviceCommand.Start, (int?)120), Assert.Single(factory.Devices.Commands));

        var unsupported = await client.PostAsJsonAsync($"/api/objects/{objectId}/device/commands", new DeviceCommandInput(DeviceCommand.ResumeSchedule, null), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadGateway, unsupported.StatusCode);

        var tooShort = await client.PostAsJsonAsync($"/api/objects/{objectId}/device/commands", new DeviceCommandInput(DeviceCommand.Start, 1), TestJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
    }

    [Fact]
    public async Task Cutting_height_is_checked_against_device_range()
    {
        await Link();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PutAsJsonAsync($"/api/objects/{objectId}/device/cutting-height", new CuttingHeightInput(6))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/objects/{objectId}/device/cutting-height", new CuttingHeightInput(12))).StatusCode);
        Assert.Equal([6], factory.Devices.CuttingHeights);
    }

    [Fact]
    public async Task Manual_refresh_is_rate_limited()
    {
        await Link();
        var calls = factory.Devices.StatusCalls;
        await client.PostAsync($"/api/objects/{objectId}/device/refresh", null);
        Assert.Equal(calls, factory.Devices.StatusCalls);

        factory.Time.Advance(DeviceService.MinimumRefreshGap);
        await client.PostAsync($"/api/objects/{objectId}/device/refresh", null);
        Assert.Equal(calls + 1, factory.Devices.StatusCalls);
    }

    [Fact]
    public async Task Device_error_creates_exactly_one_task_with_push()
    {
        await Link();
        factory.Devices.Status = FakeDeviceProvider.Broken();

        var taskId = await RefreshAsTheMonitorWould();
        Assert.NotEqual(0, taskId);
        var task = await (await client.GetAsync($"/api/tasks/{taskId}")).ReadAsync<GardenTaskDto>();
        Assert.Equal("Robi: Angehoben (Code 15)", task.Title);
        Assert.Equal(objectId, task.ObjectId);
        Assert.Contains(factory.Push.Sent, s => s.Message.Url == $"/aufgaben/{taskId}");

        Assert.Equal(taskId, await RefreshAsTheMonitorWould());
        Assert.Single(await (await client.GetAsync("/api/tasks")).ReadAsync<List<GardenTaskDto>>());

        // Erst wenn die Aufgabe erledigt ist, darf ein weiterhin bestehender Fehler eine neue erzeugen.
        await client.PostAsync($"/api/tasks/{taskId}/complete", null);
        Assert.NotEqual(taskId, await RefreshAsTheMonitorWould());
    }

    [Fact]
    public async Task Error_tasks_can_be_switched_off()
    {
        await Link(createTasks: false);
        factory.Devices.Status = FakeDeviceProvider.Broken();
        Assert.Equal(0, await RefreshAsTheMonitorWould());
        Assert.Empty(await (await client.GetAsync("/api/tasks")).ReadAsync<List<GardenTaskDto>>());
    }
}
