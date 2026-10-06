using System.Text.Json;
using Kuestencode.Beetwerk.Api.Configuration;
using WebPush;

namespace Kuestencode.Beetwerk.Api.Push;

public sealed record VapidKeys(string PublicKey, string PrivateKey);

/// <summary>
/// VAPID-Schlüssel aus der Konfiguration. Fehlen sie, werden einmalig welche erzeugt und im Datenverzeichnis
/// abgelegt – sonst würden bei jedem Neustart alle Push-Abos ungültig.
/// </summary>
public class VapidKeyStore(BeetwerkOptions options, ILogger<VapidKeyStore> logger)
{
    private readonly Lazy<VapidKeys> keys = new(() => Load(options, logger));

    public VapidKeys Keys => keys.Value;

    public static VapidKeys Generate()
    {
        var generated = VapidHelper.GenerateVapidKeys();
        return new VapidKeys(generated.PublicKey, generated.PrivateKey);
    }

    private static VapidKeys Load(BeetwerkOptions options, ILogger logger)
    {
        if (options.VapidPublicKey is not null && options.VapidPrivateKey is not null)
            return new VapidKeys(options.VapidPublicKey, options.VapidPrivateKey);

        var path = Path.Combine(options.KeyDirectory, "vapid.json");
        if (File.Exists(path))
            return JsonSerializer.Deserialize<VapidKeys>(File.ReadAllText(path))!;

        var created = Generate();
        Directory.CreateDirectory(options.KeyDirectory);
        File.WriteAllText(path, JsonSerializer.Serialize(created));
        logger.LogInformation("Keine VAPID-Schlüssel konfiguriert – neue Schlüssel wurden in {Path} gespeichert.", path);
        return created;
    }
}
