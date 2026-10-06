namespace Kuestencode.Beetwerk.Api.Configuration;

public enum AuthMode
{
    Login,
    None
}

public sealed record TileSource(string Url, string Attribution, int MaxZoom);

/// <summary>Alle Einstellungen kommen aus flachen Schlüsseln (Umgebungsvariablen aus der .env oder appsettings.json).</summary>
public sealed class BeetwerkOptions
{
    public string Version { get; init; } = "dev";
    public AuthMode AuthMode { get; init; } = AuthMode.Login;
    public string? PublicUrl { get; init; }
    public bool AllowNoneAuthOnPublicUrl { get; init; }
    public string? AdminUsername { get; init; }
    public string? AdminPassword { get; init; }
    public required string DataDirectory { get; init; }
    public bool TrustForwardedHeaders { get; init; } = true;

    public bool PushRequested { get; init; } = true;
    public string? VapidPublicKey { get; init; }
    public string? VapidPrivateKey { get; init; }
    public string VapidSubject { get; init; } = "mailto:admin@localhost";
    public int NotifyHour { get; init; } = 8;

    public required TileSource PrimaryTiles { get; init; }
    public TileSource? FallbackTiles { get; init; }

    public string DatabasePath => Path.Combine(DataDirectory, "beetwerk.db");
    public string UploadDirectory => Path.Combine(DataDirectory, "uploads");
    public string KeyDirectory => Path.Combine(DataDirectory, "keys");

    /// <summary>Web Push braucht HTTPS. Bei explizit unverschlüsselter öffentlicher Adresse wird Push daher abgeschaltet.</summary>
    public bool PushEnabled => PushRequested && !IsInsecurePublicUrl;

    public bool IsInsecurePublicUrl =>
        Uri.TryCreate(PublicUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && !uri.IsLoopback;

    public const string DefaultPrimaryUrl =
        "https://dienste.gdi-sh.de/WMS_SH_DOP20col_OpenGBD?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=sh_dop20_rgb&STYLES=&CRS=EPSG:3857&BBOX={bbox-epsg-3857}&WIDTH=256&HEIGHT=256&FORMAT=image/png&TRANSPARENT=true";
    public const string DefaultPrimaryAttribution = "© GeoBasis-DE/LVermGeo SH/CC BY 4.0";
    public const string DefaultFallbackUrl =
        "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
    public const string DefaultFallbackAttribution = "Esri, Maxar, Earthstar Geographics, and the GIS User Community";

    public static BeetwerkOptions FromConfiguration(IConfiguration config, string contentRoot)
    {
        var authModeText = config["AUTH_MODE"];
        var authMode = string.IsNullOrWhiteSpace(authModeText)
            ? AuthMode.Login
            : Enum.TryParse<AuthMode>(authModeText, ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"Unbekannter AUTH_MODE '{authModeText}'. Erlaubt: login, none.");

        var fallbackUrl = config["MAP_FALLBACK_URL"] ?? DefaultFallbackUrl;

        return new BeetwerkOptions
        {
            // Gleiche Reihenfolge wie in der Werkbank; DOCKER_IMAGE_TAG setzt die Build-Pipeline.
            Version = NullIfEmpty(config["MODULE_VERSION"]) ?? NullIfEmpty(config["IMAGE_TAG"]) ?? NullIfEmpty(config["DOCKER_IMAGE_TAG"]) ?? "dev",
            AuthMode = authMode,
            PublicUrl = NullIfEmpty(config["PUBLIC_URL"]),
            AllowNoneAuthOnPublicUrl = Flag(config["AUTH_ALLOW_NONE_PUBLIC"], false),
            AdminUsername = NullIfEmpty(config["ADMIN_USERNAME"]),
            AdminPassword = NullIfEmpty(config["ADMIN_PASSWORD"]),
            DataDirectory = Path.GetFullPath(NullIfEmpty(config["DATA_DIR"]) ?? Path.Combine(contentRoot, "data")),
            TrustForwardedHeaders = Flag(config["TRUST_FORWARDED_HEADERS"], true),
            PushRequested = Flag(config["PUSH_ENABLED"], true),
            VapidPublicKey = NullIfEmpty(config["VAPID_PUBLIC_KEY"]),
            VapidPrivateKey = NullIfEmpty(config["VAPID_PRIVATE_KEY"]),
            VapidSubject = NullIfEmpty(config["VAPID_SUBJECT"]) ?? "mailto:admin@localhost",
            NotifyHour = int.TryParse(config["NOTIFY_HOUR"], out var hour) && hour is >= 0 and <= 23 ? hour : 8,
            PrimaryTiles = new TileSource(
                NullIfEmpty(config["MAP_PRIMARY_URL"]) ?? DefaultPrimaryUrl,
                NullIfEmpty(config["MAP_PRIMARY_ATTRIBUTION"]) ?? DefaultPrimaryAttribution,
                int.TryParse(config["MAP_PRIMARY_MAXZOOM"], out var primaryZoom) ? primaryZoom : 21),
            FallbackTiles = string.IsNullOrWhiteSpace(fallbackUrl) || fallbackUrl == "none"
                ? null
                : new TileSource(
                    fallbackUrl,
                    NullIfEmpty(config["MAP_FALLBACK_ATTRIBUTION"]) ?? DefaultFallbackAttribution,
                    int.TryParse(config["MAP_FALLBACK_MAXZOOM"], out var fallbackZoom) ? fallbackZoom : 19)
        };
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Flag(string? value, bool fallback) =>
        bool.TryParse(value, out var result) ? result : fallback;
}
