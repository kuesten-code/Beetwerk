using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Domain.Devices;

namespace Kuestencode.Beetwerk.Api.Devices;

/// <summary>
/// Husqvarna Automower Connect API (developer.husqvarnagroup.cloud). Anmeldung per Client Credentials;
/// alle Mäher eines Kontos kommen in einer Antwort, daher wird die Liste kurz zwischengespeichert.
/// </summary>
public sealed class HusqvarnaProvider(IHttpClientFactory httpClients, BeetwerkOptions options, TimeProvider time, ILogger<HusqvarnaProvider> logger)
    : IDeviceProvider
{
    public const string ProviderKey = "husqvarna";
    public const string HttpClientName = "husqvarna";
    public const string TokenUrl = "https://api.authentication.husqvarnagroup.dev/v1/oauth2/token";
    public const string ApiBase = "https://api.amc.husqvarna.dev/v1";
    private const string JsonApi = "application/vnd.api+json";
    private static readonly TimeSpan ListCacheAge = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? accessToken;
    private DateTimeOffset tokenExpires;
    private JsonElement? mowers;
    private DateTimeOffset mowersFetched;

    public string Key => ProviderKey;
    public string DisplayName => "Husqvarna Automower";
    public bool IsConfigured => options.HusqvarnaClientId is not null && options.HusqvarnaClientSecret is not null;
    public TimeSpan PollInterval => options.HusqvarnaPollInterval;

    public IReadOnlySet<DeviceCommand> SupportedCommands { get; } = new HashSet<DeviceCommand>(Enum.GetValues<DeviceCommand>());

    public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct)
    {
        var data = await GetMowersAsync(forceRefresh: true, ct);
        return data.EnumerateArray()
            .Select(m =>
            {
                var system = m.GetProperty("attributes").GetProperty("system");
                return new DeviceInfo(m.GetProperty("id").GetString()!, Text(system, "name") ?? "Automower", Text(system, "model"));
            })
            .ToList();
    }

    public async Task<DeviceStatus?> GetStatusAsync(DeviceRef device, CancellationToken ct)
    {
        var data = await GetMowersAsync(forceRefresh: false, ct);
        foreach (var mower in data.EnumerateArray())
        {
            if (mower.GetProperty("id").GetString() == device.ExternalId)
                return MapStatus(mower, TimeZoneInfo.Local, time.GetUtcNow());
        }
        return null;
    }

    public async Task SendCommandAsync(DeviceRef device, DeviceCommand command, int? durationMinutes, CancellationToken ct)
    {
        var body = CommandBody(command, durationMinutes);
        await SendAsync(HttpMethod.Post, $"/mowers/{Uri.EscapeDataString(device.ExternalId)}/actions", body, ct);
        mowers = null;
    }

    public async Task SetCuttingHeightAsync(DeviceRef device, int height, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["data"] = new JsonObject { ["type"] = "settings", ["attributes"] = new JsonObject { ["cuttingHeight"] = height } }
        };
        await SendAsync(HttpMethod.Post, $"/mowers/{Uri.EscapeDataString(device.ExternalId)}/settings", body, ct);
        mowers = null;
    }

    public static JsonObject CommandBody(DeviceCommand command, int? durationMinutes)
    {
        var (type, needsDuration) = command switch
        {
            DeviceCommand.Start => ("Start", true),
            DeviceCommand.Pause => ("Pause", false),
            DeviceCommand.ParkUntilFurtherNotice => ("ParkUntilFurtherNotice", false),
            DeviceCommand.ParkUntilNextSchedule => ("ParkUntilNextSchedule", false),
            DeviceCommand.ResumeSchedule => ("ResumeSchedule", false),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        var data = new JsonObject { ["type"] = type };
        if (needsDuration)
            data["attributes"] = new JsonObject { ["duration"] = durationMinutes ?? 180 };
        return new JsonObject { ["data"] = data };
    }

    /// <summary>Übersetzt einen Mäher aus der API-Antwort in den plattformneutralen Status.</summary>
    public static DeviceStatus MapStatus(JsonElement mower, TimeZoneInfo mowerTimeZone, DateTimeOffset now)
    {
        var attributes = mower.GetProperty("attributes");
        var system = attributes.GetProperty("system");
        var state = attributes.TryGetProperty("mower", out var m) ? m : default;
        var stateText = Text(state, "state");
        var activityText = Text(state, "activity");
        var connected = !attributes.TryGetProperty("metadata", out var metadata)
                        || !metadata.TryGetProperty("connected", out var c) || c.ValueKind != JsonValueKind.False;
        var errorCode = Number(state, "errorCode") is { } code and > 0 ? code : (int?)null;

        var activity = (stateText, activityText) switch
        {
            ("ERROR" or "FATAL_ERROR" or "ERROR_AT_POWER_UP", _) => DeviceActivity.Error,
            _ when !connected => DeviceActivity.Offline,
            ("PAUSED", _) => DeviceActivity.Paused,
            (_, "MOWING") => DeviceActivity.Mowing,
            (_, "GOING_HOME") => DeviceActivity.GoingHome,
            (_, "CHARGING") => DeviceActivity.Charging,
            (_, "LEAVING") => DeviceActivity.Leaving,
            (_, "PARKED_IN_CS") => DeviceActivity.Parked,
            (_, "STOPPED_IN_GARDEN") => DeviceActivity.Stopped,
            ("STOPPED" or "OFF", _) => DeviceActivity.Stopped,
            _ => DeviceActivity.Unknown
        };

        double? latitude = null, longitude = null;
        if (attributes.TryGetProperty("positions", out var positions) && positions.ValueKind == JsonValueKind.Array && positions.GetArrayLength() > 0)
        {
            // Die API liefert die jüngste Position zuerst.
            latitude = positions[0].GetProperty("latitude").GetDouble();
            longitude = positions[0].GetProperty("longitude").GetDouble();
        }

        long? nextStart = attributes.TryGetProperty("planner", out var planner)
                          && planner.TryGetProperty("nextStartTimestamp", out var ns) && ns.ValueKind == JsonValueKind.Number
            ? ns.GetInt64()
            : null;

        return new DeviceStatus
        {
            ExternalId = mower.GetProperty("id").GetString()!,
            Name = Text(system, "name") ?? "Automower",
            Activity = activity,
            RawState = $"{stateText ?? "?"} / {activityText ?? "?"}",
            BatteryPercent = attributes.TryGetProperty("battery", out var battery) ? Number(battery, "batteryPercent") : null,
            CuttingHeight = attributes.TryGetProperty("settings", out var settings) ? Number(settings, "cuttingHeight") : null,
            CuttingHeightMin = 1,
            CuttingHeightMax = 9,
            ErrorCode = activity == DeviceActivity.Error ? errorCode : null,
            ErrorText = activity == DeviceActivity.Error ? HusqvarnaErrors.Describe(errorCode) : null,
            NextStart = nextStart is > 0 ? FromMowerTimestamp(nextStart.Value, mowerTimeZone) : null,
            Latitude = latitude,
            Longitude = longitude,
            Connected = connected,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Zeitstempel des Mähers sind Millisekunden, aber auf die Ortszeit des Mähers bezogen statt auf UTC
    /// (so dokumentiert auch aioautomower). Die Ziffern sind also die lokale Uhrzeit.
    /// </summary>
    public static DateTimeOffset FromMowerTimestamp(long milliseconds, TimeZoneInfo mowerTimeZone)
    {
        var wallClock = DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(wallClock, mowerTimeZone.GetUtcOffset(wallClock));
    }

    private async Task<JsonElement> GetMowersAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && mowers is { } cached && time.GetUtcNow() - mowersFetched < ListCacheAge)
            return cached;

        using var document = await SendAsync(HttpMethod.Get, "/mowers", null, ct);
        var data = document!.RootElement.GetProperty("data").Clone();
        mowers = data;
        mowersFetched = time.GetUtcNow();
        return data;
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new DeviceProviderException("Husqvarna ist nicht eingerichtet (HUSQVARNA_CLIENT_ID / HUSQVARNA_CLIENT_SECRET fehlen).");

        var token = await GetTokenAsync(ct);
        using var request = new HttpRequestMessage(method, ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Api-Key", options.HusqvarnaClientId);
        request.Headers.Add("Authorization-Provider", "husqvarna");
        request.Headers.Accept.ParseAdd(JsonApi);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, JsonApi);

        HttpResponseMessage response;
        try
        {
            response = await httpClients.CreateClient(HttpClientName).SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new DeviceProviderException("Husqvarna ist nicht erreichbar.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                accessToken = null;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Husqvarna {Method} {Path}: {Status} {Body}", method, path, (int)response.StatusCode, text);
                throw new DeviceProviderException($"Husqvarna hat abgelehnt ({(int)response.StatusCode}): {ErrorDetail(text)}");
            }
            return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await tokenLock.WaitAsync(ct);
        try
        {
            if (accessToken is not null && time.GetUtcNow() < tokenExpires)
                return accessToken;

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.HusqvarnaClientId!,
                ["client_secret"] = options.HusqvarnaClientSecret!
            });
            using var response = await httpClients.CreateClient(HttpClientName).PostAsync(TokenUrl, content, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new DeviceProviderException($"Anmeldung bei Husqvarna fehlgeschlagen ({(int)response.StatusCode}). Application Key und Secret prüfen.");

            using var document = JsonDocument.Parse(text);
            accessToken = document.RootElement.GetProperty("access_token").GetString();
            var lifetime = document.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            // Eine Minute Puffer, damit kein Token mitten in einer Anfrage abläuft.
            tokenExpires = time.GetUtcNow().AddSeconds(Math.Max(lifetime - 60, 60));
            return accessToken!;
        }
        catch (HttpRequestException ex)
        {
            throw new DeviceProviderException("Husqvarna-Anmeldung ist nicht erreichbar.", ex);
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private static string ErrorDetail(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
                return Text(errors[0], "detail") ?? Text(errors[0], "title") ?? body;
            if (document.RootElement.TryGetProperty("message", out var message))
                return message.ToString();
        }
        catch (JsonException)
        {
            // Kein JSON – dann eben den Rohtext zeigen.
        }
        return body.Length > 200 ? body[..200] : body;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}

/// <summary>Häufige Fehlercodes der Automower-API. Unbekannte Codes werden mit Nummer angezeigt.</summary>
public static class HusqvarnaErrors
{
    private static readonly Dictionary<int, string> Texts = new()
    {
        [1] = "Außerhalb des Arbeitsbereichs",
        [2] = "Kein Schleifensignal",
        [3] = "Falsches Schleifensignal",
        [9] = "Eingeklemmt",
        [10] = "Auf dem Kopf",
        [11] = "Akku schwach",
        [12] = "Akku leer",
        [13] = "Kein Antrieb",
        [15] = "Angehoben",
        [16] = "Steckt in der Ladestation fest",
        [17] = "Ladestation blockiert",
        [18] = "Problem mit dem Stoßsensor hinten",
        [19] = "Problem mit dem Stoßsensor vorne",
        [20] = "Radmotor rechts blockiert",
        [21] = "Radmotor links blockiert",
        [24] = "Schneidsystem blockiert"
    };

    public static string Describe(int? code) => code switch
    {
        null => "Fehler",
        { } c when Texts.TryGetValue(c, out var text) => $"{text} (Code {c})",
        { } c => $"Fehlercode {c}"
    };
}
