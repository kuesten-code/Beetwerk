using Kuestencode.Beetwerk.Api.Configuration;

namespace Kuestencode.Beetwerk.Api.Services;

/// <summary>
/// Ablage hochgeladener Bilder im Datenverzeichnis. Dateinamen vergibt ausschließlich der Server,
/// damit über den Upload keine fremden Pfade erreichbar sind. Bilder werden bereits im Browser verkleinert;
/// hier wird nur noch der Dateityp anhand der Signatur geprüft.
/// </summary>
public class UploadStore(BeetwerkOptions options)
{
    public const long MaxImageBytes = 25 * 1024 * 1024;

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    public async Task<(string FileName, string ContentType)> SaveImageAsync(IFormFile file, string folder, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > MaxImageBytes)
            throw new InvalidDataException($"Das Bild muss kleiner als {MaxImageBytes / 1024 / 1024} MB sein.");

        await using var source = file.OpenReadStream();
        var header = new byte[12];
        var read = await source.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var contentType = DetectImageType(header.AsSpan(0, read))
                          ?? throw new InvalidDataException("Nur JPEG-, PNG- oder WebP-Bilder sind erlaubt.");

        var relative = Path.Combine(folder, Guid.NewGuid().ToString("N") + Extensions[contentType]);
        var target = FullPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using (var output = File.Create(target))
        {
            await output.WriteAsync(header.AsMemory(0, read), ct);
            await source.CopyToAsync(output, ct);
        }
        return (relative.Replace('\\', '/'), contentType);
    }

    public IResult Serve(string fileName, string contentType)
    {
        var path = FullPath(fileName);
        return File.Exists(path)
            ? Results.File(path, contentType, enableRangeProcessing: false)
            : Results.NotFound();
    }

    public void Delete(params string?[] fileNames)
    {
        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrEmpty(fileName))
                continue;
            var path = FullPath(fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static string? DetectImageType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "image/jpeg";
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }

    private string FullPath(string relative)
    {
        var root = Path.GetFullPath(options.UploadDirectory);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Ungültiger Dateipfad.");
        return full;
    }
}
