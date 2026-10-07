using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Domain.Devices;

namespace Kuestencode.Beetwerk.Api.Devices;

/// <summary>
/// Home Assistant über die REST-API (Long-Lived Access Token). Gerät ist eine <c>lawn_mower.*</c>-Entity;
/// Akku, Schnitthöhe und Fehler liegen in eigenen Entities, die entweder eingestellt oder nach dem
/// Namensschema der Husqvarna-Integration erraten werden (z. B. <c>number.robi_cutting_height</c>).
/// </summary>
public sealed class HomeAssistantProvider(IHttpClientFactory httpClients, BeetwerkOptions options, TimeProvider time) : IDeviceProvider
{
    public const string ProviderKey = "homeassistant";
    public const string HttpClientName = "homeassistant";
    public const string BatteryEntity = "batteryEntity";
    public const string CuttingHeightEntity = "cuttingHeightEntity";
    public const string ErrorEntity = "errorEntity";

    public string Key => ProviderKey;
    public string DisplayName => "Home Assistant";
    public bool IsConfigured => options.HomeAssistantUrl is not null && options.HomeAssistantToken is not null;
    public TimeSpan PollInterval => options.HomeAssistantPollInterval;

    /// <summary>Die lawn_mower-Plattform kennt nur Starten, Pausieren und Zurück zur Station.</summary>
    public IReadOnlySet<DeviceCommand> SupportedCommands { get; } =
        new HashSet<DeviceCommand> { DeviceCommand.Start, DeviceCommand.Pause, DeviceCommand.ParkUntilFurtherNotice };

    public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct)
    {
        using var document = await GetAsync("/api/states", ct) ?? throw new DeviceProviderException("Home Assistant hat keine Zustände geliefert.");
        return document.RootElement.EnumerateArray()
            .Where(s => s.GetProperty("entity_id").GetString()!.StartsWith("lawn_mower.", StringComparison.Ordinal))
            .Select(s => new DeviceInfo(s.GetProperty("entity_id").GetString()!, FriendlyName(s), null))
            .ToList();
    }

    public async Task<DeviceStatus?> GetStatusAsync(DeviceRef device, CancellationToken ct)
    {
        using var mower = await GetAsync($"/api/states/{device.ExternalId}", ct);
        if (mower is null)
            return null;

        using var battery = await GetAsync($"/api/states/{EntityFor(device, BatteryEntity)}", ct);
        using var cuttingHeight = await GetAsync($"/api/states/{EntityFor(device, CuttingHeightEntity)}", ct);
        using var error = await GetAsync($"/api/states/{EntityFor(device, ErrorEntity)}", ct);
        return MapStatus(device.ExternalId, mower.RootElement, battery?.RootElement, cuttingHeight?.RootElement, error?.RootElement, time.GetUtcNow());
    }

    public Task SendCommandAsync(DeviceRef device, DeviceCommand command, int? durationMinutes, CancellationToken ct)
    {
        var service = command switch
        {
            DeviceCommand.Start => "start_mowing",
            DeviceCommand.Pause => "pause",
            DeviceCommand.ParkUntilFurtherNotice => "dock",
            _ => throw new DeviceProviderException("Dieser Befehl wird über Home Assistant nicht unterstützt.")
        };
        return PostAsync($"/api/services/lawn_mower/{service}", new { entity_id = device.ExternalId }, ct);
    }

    public Task SetCuttingHeightAsync(DeviceRef device, int height, CancellationToken ct) =>
        PostAsync("/api/services/number/set_value", new { entity_id = EntityFor(device, CuttingHeightEntity), value = height }, ct);

    /// <summary>Eingestellte Entity oder die Benennung der Husqvarna-Integration (lawn_mower.robi → sensor.robi_battery).</summary>
    public static string EntityFor(DeviceRef device, string setting)
    {
        if (device.Setting(setting) is { } configured)
            return configured;
        var name = device.ExternalId.Split('.', 2)[^1];
        return setting switch
        {
            BatteryEntity => $"sensor.{name}_battery",
            CuttingHeightEntity => $"number.{name}_cutting_height",
            ErrorEntity => $"sensor.{name}_error",
            _ => throw new ArgumentOutOfRangeException(nameof(setting))
        };
    }

    public static DeviceStatus MapStatus(string entityId, JsonElement mower, JsonElement? battery, JsonElement? cuttingHeight, JsonElement? error, DateTimeOffset now)
    {
        var state = mower.GetProperty("state").GetString();
        var activity = state switch
        {
            "mowing" => DeviceActivity.Mowing,
            "docked" => DeviceActivity.Parked,
            "paused" => DeviceActivity.Paused,
            "returning" => DeviceActivity.GoingHome,
            "idle" => DeviceActivity.Stopped,
            "error" => DeviceActivity.Error,
            "unavailable" => DeviceActivity.Offline,
            _ => DeviceActivity.Unknown
        };

        var errorState = error?.GetProperty("state").GetString();
        var hasErrorText = errorState is not null and not ("no_error" or "unknown" or "unavailable" or "");

        return new DeviceStatus
        {
            ExternalId = entityId,
            Name = FriendlyName(mower),
            Activity = activity,
            RawState = state,
            BatteryPercent = NumericState(battery) is { } b ? (int)Math.Round(b) : null,
            CuttingHeight = NumericState(cuttingHeight) is { } h ? (int)Math.Round(h) : null,
            CuttingHeightMin = Attribute(cuttingHeight, "min") is { } min ? (int)min : 1,
            CuttingHeightMax = Attribute(cuttingHeight, "max") is { } max ? (int)max : 9,
            ErrorText = activity == DeviceActivity.Error ? hasErrorText ? errorState!.Replace('_', ' ') : "Fehler" : null,
            Latitude = Attribute(mower, "latitude"),
            Longitude = Attribute(mower, "longitude"),
            Connected = activity != DeviceActivity.Offline,
            UpdatedAt = now
        };
    }

    private static string FriendlyName(JsonElement state) =>
        state.TryGetProperty("attributes", out var a) && a.TryGetProperty("friendly_name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString()!
            : state.GetProperty("entity_id").GetString()!;

    private static double? NumericState(JsonElement? state) =>
        state is { } s && double.TryParse(s.GetProperty("state").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? Attribute(JsonElement? state, string name) =>
        state is { } s && s.TryGetProperty("attributes", out var a) && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

    /// <summary>Liefert null bei 404 – fehlende Zusatz-Entities sind kein Fehler.</summary>
    private async Task<JsonDocument?> GetAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, options.HomeAssistantUrl + path), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, ct);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private async Task PostAsync(string path, object body, CancellationToken ct)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, options.HomeAssistantUrl + path) { Content = JsonContent.Create(body) }, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new DeviceProviderException("Home Assistant ist nicht eingerichtet (HOMEASSISTANT_URL / HOMEASSISTANT_TOKEN fehlen).");
        using (request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.HomeAssistantToken);
            try
            {
                return await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new DeviceProviderException($"Home Assistant unter {options.HomeAssistantUrl} ist nicht erreichbar.", ex);
            }
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw response.StatusCode == HttpStatusCode.Unauthorized
            ? new DeviceProviderException("Home Assistant lehnt den Zugriff ab – Token prüfen.")
            : new DeviceProviderException($"Home Assistant meldet {(int)response.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");
    }
}
