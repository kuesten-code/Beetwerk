using System.Globalization;
using Kuestencode.Beetwerk.Api.Configuration;

namespace Kuestencode.Beetwerk.Api.Endpoints;

/// <summary>
/// Leitet Kartenkacheln über den Server weiter. Viele Geodienste (z. B. der WMS für die DOP Schleswig-Holstein)
/// senden keine CORS-Header, sodass der Browser sie nicht direkt laden darf. Nebenbei bleiben die Kacheln hinter
/// der Anmeldung und die IP-Adressen der Nutzer gehen nicht an Dritte.
/// </summary>
public static class TileEndpoints
{
    public const string HttpClientName = "tiles";
    private const double EarthHalfCircumference = 20037508.342789244;

    public static void MapTileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tiles/{source}/{z:int}/{x:int}/{y:int}", async (string source, int z, int x, int y,
            BeetwerkOptions options, IHttpClientFactory httpClients, HttpContext context) =>
        {
            var tiles = source switch
            {
                "primary" => options.PrimaryTiles,
                "fallback" => options.FallbackTiles,
                _ => null
            };
            if (tiles is null || z is < 0 or > 24 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z)
                return Results.NotFound();

            var client = httpClients.CreateClient(HttpClientName);
            try
            {
                using var upstream = await client.GetAsync(BuildUrl(tiles.Url, z, x, y), context.RequestAborted);
                if (!upstream.IsSuccessStatusCode)
                    return Results.StatusCode(upstream.StatusCode == System.Net.HttpStatusCode.NotFound ? 404 : 502);

                var bytes = await upstream.Content.ReadAsByteArrayAsync(context.RequestAborted);
                context.Response.Headers.CacheControl = "private, max-age=604800";
                return Results.Bytes(bytes, upstream.Content.Headers.ContentType?.ToString() ?? "image/png");
            }
            catch (HttpRequestException)
            {
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }
            catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
            }
        });
    }

    /// <summary>Für die Oberfläche: Kachelquellen zeigen immer auf den eigenen Proxy.</summary>
    public static TileSource ToProxied(TileSource source, string name) =>
        source with { Url = $"/tiles/{name}/{{z}}/{{x}}/{{y}}" };

    public static string BuildUrl(string template, int z, int x, int y)
    {
        var url = template
            .Replace("{z}", z.ToString(CultureInfo.InvariantCulture))
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
            .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));
        return url.Contains("{bbox-epsg-3857}") ? url.Replace("{bbox-epsg-3857}", BoundingBox3857(z, x, y)) : url;
    }

    public static string BoundingBox3857(int z, int x, int y)
    {
        var size = 2 * EarthHalfCircumference / (1 << z);
        var minX = -EarthHalfCircumference + x * size;
        var maxY = EarthHalfCircumference - y * size;
        return string.Join(',', new[] { minX, maxY - size, minX + size, maxY }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    }
}
