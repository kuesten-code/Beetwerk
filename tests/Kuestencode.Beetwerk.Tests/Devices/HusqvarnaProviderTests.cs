using System.Net;
using System.Text.Json;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Devices;
using Kuestencode.Beetwerk.Domain.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Kuestencode.Beetwerk.Tests.Devices;

public class HusqvarnaProviderTests
{
    private const string Mowers = """
        {"data":[{"type":"mower","id":"m-1","attributes":{
          "system":{"name":"Robi","model":"Automower 430X","serialNumber":123},
          "battery":{"batteryPercent":77},
          "mower":{"mode":"MAIN_AREA","activity":"MOWING","state":"IN_OPERATION","errorCode":0,"errorCodeTimestamp":0},
          "planner":{"nextStartTimestamp":1783418400000,"override":{"action":"NOT_ACTIVE"},"restrictedReason":"NONE"},
          "metadata":{"connected":true,"statusTimestamp":1783410000000},
          "positions":[{"latitude":54.4719,"longitude":8.9099},{"latitude":54.4718,"longitude":8.9098}],
          "settings":{"cuttingHeight":5,"headlight":{"mode":"EVENING_ONLY"}}
        }}]}
        """;

    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero));

    private static (HusqvarnaProvider Provider, FakeHttp Http) Create(bool configured = true)
    {
        var http = new FakeHttp()
            .On(HttpMethod.Post, HusqvarnaProvider.TokenUrl, """{"access_token":"tok-1","expires_in":86399,"token_type":"Bearer"}""")
            .On(HttpMethod.Get, HusqvarnaProvider.ApiBase + "/mowers", Mowers);
        var settings = configured
            ? new Dictionary<string, string?> { ["HUSQVARNA_CLIENT_ID"] = "app-key", ["HUSQVARNA_CLIENT_SECRET"] = "app-secret" }
            : [];
        var options = BeetwerkOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), Path.GetTempPath());
        return (new HusqvarnaProvider(http, options, Time, NullLogger<HusqvarnaProvider>.Instance), http);
    }

    private static readonly DeviceRef Robi = new("m-1", new Dictionary<string, string>());

    [Fact]
    public async Task Lists_mowers_with_client_credentials_and_required_headers()
    {
        var (provider, http) = Create();

        var devices = await provider.ListDevicesAsync(CancellationToken.None);

        var mower = Assert.Single(devices);
        Assert.Equal(new DeviceInfo("m-1", "Robi", "Automower 430X"), mower);

        var token = http.Requests[0];
        Assert.Contains("grant_type=client_credentials", token.Body);
        Assert.Contains("client_id=app-key", token.Body);
        Assert.Contains("client_secret=app-secret", token.Body);

        var api = http.Requests[1];
        Assert.Equal("Bearer tok-1", api.Headers["Authorization"]);
        Assert.Equal("app-key", api.Headers["X-Api-Key"]);
        Assert.Equal("husqvarna", api.Headers["Authorization-Provider"]);
    }

    [Fact]
    public async Task Token_and_mower_list_are_reused()
    {
        var (provider, http) = Create();
        await provider.GetStatusAsync(Robi, CancellationToken.None);
        await provider.GetStatusAsync(Robi, CancellationToken.None);

        Assert.Single(http.Requests, r => r.Url == HusqvarnaProvider.TokenUrl);
        Assert.Single(http.Requests, r => r.Url.EndsWith("/mowers"));
    }

    [Fact]
    public async Task Maps_status()
    {
        var (provider, _) = Create();
        var status = (await provider.GetStatusAsync(Robi, CancellationToken.None))!;

        Assert.Equal("Robi", status.Name);
        Assert.Equal(DeviceActivity.Mowing, status.Activity);
        Assert.Equal(77, status.BatteryPercent);
        Assert.Equal(5, status.CuttingHeight);
        Assert.Equal((1, 9), (status.CuttingHeightMin, status.CuttingHeightMax));
        Assert.Equal(54.4719, status.Latitude);
        Assert.Null(status.ErrorText);
        Assert.False(status.HasError);
    }

    [Fact]
    public async Task Unknown_mower_has_no_status()
    {
        var (provider, _) = Create();
        Assert.Null(await provider.GetStatusAsync(new DeviceRef("other", new Dictionary<string, string>()), CancellationToken.None));
    }

    [Theory]
    [InlineData("IN_OPERATION", "CHARGING", true, DeviceActivity.Charging)]
    [InlineData("RESTRICTED", "PARKED_IN_CS", true, DeviceActivity.Parked)]
    [InlineData("IN_OPERATION", "GOING_HOME", true, DeviceActivity.GoingHome)]
    [InlineData("PAUSED", "STOPPED_IN_GARDEN", true, DeviceActivity.Paused)]
    [InlineData("STOPPED", "NOT_APPLICABLE", true, DeviceActivity.Stopped)]
    [InlineData("ERROR", "STOPPED_IN_GARDEN", true, DeviceActivity.Error)]
    [InlineData("FATAL_ERROR", "UNKNOWN", false, DeviceActivity.Error)]
    [InlineData("IN_OPERATION", "MOWING", false, DeviceActivity.Offline)]
    [InlineData("UNKNOWN", "UNKNOWN", true, DeviceActivity.Unknown)]
    public void Maps_states_and_activities(string state, string activity, bool connected, DeviceActivity expected)
    {
        var json = """
            {"id":"m-1","attributes":{"system":{"name":"Robi"},"mower":{"state":"STATE","activity":"ACTIVITY","errorCode":15},
             "metadata":{"connected":CONNECTED}}}
            """.Replace("STATE", state).Replace("ACTIVITY", activity).Replace("CONNECTED", connected ? "true" : "false");
        var status = HusqvarnaProvider.MapStatus(JsonDocument.Parse(json).RootElement, TimeZoneInfo.Utc, Time.GetUtcNow());
        Assert.Equal(expected, status.Activity);
        if (expected == DeviceActivity.Error)
        {
            Assert.Equal(15, status.ErrorCode);
            Assert.Equal("Angehoben (Code 15)", status.ErrorText);
        }
        else
        {
            Assert.Null(status.ErrorCode);
        }
    }

    [Fact]
    public void Mower_timestamps_are_local_wall_clock_time()
    {
        // 1783418400000 ms = 2026-07-07 10:00 „UTC“ – gemeint ist 10:00 Ortszeit des Mähers (Sommerzeit, +02:00).
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var start = HusqvarnaProvider.FromMowerTimestamp(1783418400000, berlin);
        Assert.Equal(new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.FromHours(2)), start);
    }

    [Theory]
    [InlineData(DeviceCommand.Start, 90, """{"data":{"type":"Start","attributes":{"duration":90}}}""")]
    [InlineData(DeviceCommand.Pause, null, """{"data":{"type":"Pause"}}""")]
    [InlineData(DeviceCommand.ParkUntilFurtherNotice, null, """{"data":{"type":"ParkUntilFurtherNotice"}}""")]
    [InlineData(DeviceCommand.ParkUntilNextSchedule, null, """{"data":{"type":"ParkUntilNextSchedule"}}""")]
    [InlineData(DeviceCommand.ResumeSchedule, null, """{"data":{"type":"ResumeSchedule"}}""")]
    public async Task Sends_actions_in_json_api_format(DeviceCommand command, int? duration, string expectedBody)
    {
        var (provider, http) = Create();
        http.On(HttpMethod.Post, HusqvarnaProvider.ApiBase + "/mowers/m-1/actions", "", HttpStatusCode.Accepted);

        await provider.SendCommandAsync(Robi, command, duration, CancellationToken.None);

        var request = http.Requests.Last();
        Assert.Equal(expectedBody, request.Body);
        Assert.StartsWith("application/vnd.api+json", request.Headers["Content-Type"]);
    }

    [Fact]
    public async Task Sets_cutting_height_via_settings()
    {
        var (provider, http) = Create();
        http.On(HttpMethod.Post, HusqvarnaProvider.ApiBase + "/mowers/m-1/settings", "", HttpStatusCode.Accepted);

        await provider.SetCuttingHeightAsync(Robi, 6, CancellationToken.None);

        Assert.Equal("""{"data":{"type":"settings","attributes":{"cuttingHeight":6}}}""", http.Requests.Last().Body);
    }

    [Fact]
    public async Task Rejected_command_reports_api_error_detail()
    {
        var (provider, http) = Create();
        http.On(HttpMethod.Post, HusqvarnaProvider.ApiBase + "/mowers/m-1/actions",
            """{"errors":[{"status":"400","title":"Bad Request","detail":"Mower is not connected"}]}""", HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<DeviceProviderException>(() => provider.SendCommandAsync(Robi, DeviceCommand.Pause, null, CancellationToken.None));
        Assert.Contains("Mower is not connected", error.Message);
    }

    [Fact]
    public async Task Wrong_credentials_give_clear_message()
    {
        var (provider, http) = Create();
        http.On(HttpMethod.Post, HusqvarnaProvider.TokenUrl, """{"error":"invalid_client"}""", HttpStatusCode.BadRequest);

        var error = await Assert.ThrowsAsync<DeviceProviderException>(() => provider.ListDevicesAsync(CancellationToken.None));
        Assert.Contains("Application Key und Secret prüfen", error.Message);
    }

    [Fact]
    public async Task Missing_configuration_is_reported()
    {
        var (provider, _) = Create(configured: false);
        Assert.False(provider.IsConfigured);
        await Assert.ThrowsAsync<DeviceProviderException>(() => provider.ListDevicesAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(null, "Fehler")]
    [InlineData(2, "Kein Schleifensignal (Code 2)")]
    [InlineData(999, "Fehlercode 999")]
    public void Error_codes_are_described(int? code, string expected)
    {
        Assert.Equal(expected, HusqvarnaErrors.Describe(code));
    }
}
