using System.Net;
using System.Text.Json;
using Kuestencode.Beetwerk.Api.Endpoints;

namespace Kuestencode.Beetwerk.Tests.Api;

public class TileTests
{
    [Fact]
    public void Xyz_placeholders_are_replaced()
    {
        Assert.Equal("https://t.example/5/3/7.png", TileEndpoints.BuildUrl("https://t.example/{z}/{x}/{y}.png", 5, 3, 7));
    }

    [Fact]
    public void Wms_bbox_is_computed_in_web_mercator()
    {
        Assert.Equal("-20037508.342789244,-20037508.342789244,20037508.342789244,20037508.342789244", TileEndpoints.BoundingBox3857(0, 0, 0));
        Assert.Equal("0,0,20037508.342789244,20037508.342789244", TileEndpoints.BoundingBox3857(1, 1, 0));
        Assert.Contains("BBOX=0,0,", TileEndpoints.BuildUrl("https://wms.example?BBOX={bbox-epsg-3857}", 1, 1, 0));
    }

    [Fact]
    public async Task Tiles_require_login_and_validate_coordinates()
    {
        using var factory = new BeetwerkFactory();
        Assert.Equal(HttpStatusCode.Redirect, (await factory.CreateBrowser().GetAsync("/tiles/primary/1/0/0")).StatusCode);

        var client = await factory.CreateLoggedInClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/tiles/primary/1/2/0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/tiles/unknown/1/0/0")).StatusCode);
    }

    [Fact]
    public async Task Config_points_tile_sources_to_proxy()
    {
        using var factory = new BeetwerkFactory();
        var client = await factory.CreateLoggedInClientAsync();
        var config = await (await client.GetAsync("/api/config")).ReadAsync<JsonElement>();
        Assert.Equal("/tiles/primary/{z}/{x}/{y}", config.GetProperty("map").GetProperty("primary").GetProperty("url").GetString());
        Assert.Equal("/tiles/fallback/{z}/{x}/{y}", config.GetProperty("map").GetProperty("fallback").GetProperty("url").GetString());
    }
}
