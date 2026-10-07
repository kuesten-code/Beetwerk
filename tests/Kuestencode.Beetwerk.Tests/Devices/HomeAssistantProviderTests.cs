using System.Net;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Devices;
using Kuestencode.Beetwerk.Domain.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace Kuestencode.Beetwerk.Tests.Devices;

public class HomeAssistantProviderTests
{
    private const string Base = "http://ha.local:8123";
    private static readonly DeviceRef Robi = new("lawn_mower.robi", new Dictionary<string, string>());

    private static (HomeAssistantProvider Provider, FakeHttp Http) Create()
    {
        var http = new FakeHttp()
            .On(HttpMethod.Get, Base + "/api/states", """
                [{"entity_id":"lawn_mower.robi","state":"mowing","attributes":{"friendly_name":"Robi"}},
                 {"entity_id":"sensor.robi_battery","state":"81","attributes":{}}]
                """)
            .On(HttpMethod.Get, Base + "/api/states/lawn_mower.robi", """{"entity_id":"lawn_mower.robi","state":"error","attributes":{"friendly_name":"Robi"}}""")
            .On(HttpMethod.Get, Base + "/api/states/sensor.robi_battery", """{"entity_id":"sensor.robi_battery","state":"81.0","attributes":{}}""")
            .On(HttpMethod.Get, Base + "/api/states/number.robi_cutting_height", """{"entity_id":"number.robi_cutting_height","state":"4","attributes":{"min":1,"max":9}}""")
            .On(HttpMethod.Get, Base + "/api/states/sensor.robi_error", """{"entity_id":"sensor.robi_error","state":"mower_tilted","attributes":{}}""");
        var options = BeetwerkOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["HOMEASSISTANT_URL"] = Base + "/",
            ["HOMEASSISTANT_TOKEN"] = "llat"
        }).Build(), Path.GetTempPath());
        return (new HomeAssistantProvider(http, options, new FakeTimeProvider()), http);
    }

    [Fact]
    public async Task Lists_only_lawn_mower_entities()
    {
        var (provider, http) = Create();
        var device = Assert.Single(await provider.ListDevicesAsync(CancellationToken.None));
        Assert.Equal(new DeviceInfo("lawn_mower.robi", "Robi", null), device);
        Assert.Equal("Bearer llat", http.Requests[0].Headers["Authorization"]);
    }

    [Fact]
    public async Task Status_combines_mower_battery_cutting_height_and_error_entities()
    {
        var (provider, _) = Create();
        var status = (await provider.GetStatusAsync(Robi, CancellationToken.None))!;

        Assert.Equal(DeviceActivity.Error, status.Activity);
        Assert.Equal("mower tilted", status.ErrorText);
        Assert.Equal(81, status.BatteryPercent);
        Assert.Equal(4, status.CuttingHeight);
        Assert.Equal("Robi", status.Name);
    }

    [Fact]
    public async Task Missing_helper_entities_are_tolerated()
    {
        var (provider, _) = Create();
        var custom = new DeviceRef("lawn_mower.robi", new Dictionary<string, string> { [HomeAssistantProvider.BatteryEntity] = "sensor.nicht_da" });
        var status = (await provider.GetStatusAsync(custom, CancellationToken.None))!;
        Assert.Null(status.BatteryPercent);
    }

    [Theory]
    [InlineData(DeviceCommand.Start, "/api/services/lawn_mower/start_mowing")]
    [InlineData(DeviceCommand.Pause, "/api/services/lawn_mower/pause")]
    [InlineData(DeviceCommand.ParkUntilFurtherNotice, "/api/services/lawn_mower/dock")]
    public async Task Commands_call_lawn_mower_services(DeviceCommand command, string path)
    {
        var (provider, http) = Create();
        http.On(HttpMethod.Post, Base + path, "[]");

        await provider.SendCommandAsync(Robi, command, 60, CancellationToken.None);

        Assert.Equal("""{"entity_id":"lawn_mower.robi"}""", http.Requests.Last().Body);
    }

    [Fact]
    public async Task Unsupported_command_is_rejected()
    {
        var (provider, _) = Create();
        Assert.DoesNotContain(DeviceCommand.ResumeSchedule, provider.SupportedCommands);
        await Assert.ThrowsAsync<DeviceProviderException>(() => provider.SendCommandAsync(Robi, DeviceCommand.ResumeSchedule, null, CancellationToken.None));
    }

    [Fact]
    public async Task Cutting_height_uses_number_entity()
    {
        var (provider, http) = Create();
        http.On(HttpMethod.Post, Base + "/api/services/number/set_value", "[]");

        await provider.SetCuttingHeightAsync(Robi, 7, CancellationToken.None);

        Assert.Equal("""{"entity_id":"number.robi_cutting_height","value":7}""", http.Requests.Last().Body);
    }

    [Fact]
    public async Task Unauthorized_token_gives_clear_message()
    {
        var (provider, http) = Create();
        http.On(HttpMethod.Get, Base + "/api/states", "", HttpStatusCode.Unauthorized);
        var error = await Assert.ThrowsAsync<DeviceProviderException>(() => provider.ListDevicesAsync(CancellationToken.None));
        Assert.Contains("Token", error.Message);
    }

    [Theory]
    [InlineData("lawn_mower.robi", HomeAssistantProvider.BatteryEntity, "sensor.robi_battery")]
    [InlineData("lawn_mower.robi", HomeAssistantProvider.CuttingHeightEntity, "number.robi_cutting_height")]
    [InlineData("lawn_mower.robi", HomeAssistantProvider.ErrorEntity, "sensor.robi_error")]
    public void Guesses_helper_entities_from_mower_name(string mower, string setting, string expected)
    {
        Assert.Equal(expected, HomeAssistantProvider.EntityFor(new DeviceRef(mower, new Dictionary<string, string>()), setting));
    }
}
