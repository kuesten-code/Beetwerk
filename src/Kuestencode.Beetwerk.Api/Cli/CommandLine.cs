using Kuestencode.Beetwerk.Api.Auth;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Push;
using Kuestencode.Beetwerk.Data;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Cli;

/// <summary>
/// Verwaltungsbefehle, die ohne Webserver laufen, z. B. im Container:
/// <c>docker compose exec beetwerk dotnet Kuestencode.Beetwerk.Api.dll user set anna</c>.
/// </summary>
public static class CommandLine
{
    public static readonly string[] Commands = ["user", "vapid", "healthcheck"];

    public static bool IsCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0]);

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextReader input)
    {
        switch (args)
        {
            case ["vapid", ..]:
                var keys = VapidKeyStore.Generate();
                await output.WriteLineAsync($"VAPID_PUBLIC_KEY={keys.PublicKey}");
                await output.WriteLineAsync($"VAPID_PRIVATE_KEY={keys.PrivateKey}");
                return 0;
            case ["healthcheck", ..]:
                return await HealthCheckAsync(args.Length > 1 ? args[1] : "http://127.0.0.1:8080/health");
            case ["user", "list"]:
            case ["user", "set", _, ..]:
            case ["user", "delete", _]:
                return await RunUserCommandAsync(args, output, input);
            default:
                await output.WriteLineAsync(Usage);
                return 2;
        }
    }

    private const string Usage = """
        Befehle:
          user list                        Alle Nutzer anzeigen
          user set <name> [passwort]       Nutzer anlegen oder Passwort setzen (ohne Passwort: Eingabe abfragen)
          user delete <name>               Nutzer löschen
          vapid                            Neues VAPID-Schlüsselpaar für die .env ausgeben
          healthcheck [url]                Exit-Code 0, wenn die App antwortet
        """;

    private static async Task<int> RunUserCommandAsync(string[] args, TextWriter output, TextReader input)
    {
        // Gleiche Quellen und gleiches Basisverzeichnis wie der Webhost, damit dieselbe Datenbank getroffen wird.
        var contentRoot = Directory.GetCurrentDirectory();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var options = BeetwerkOptions.FromConfiguration(configuration, contentRoot);
        Directory.CreateDirectory(options.DataDirectory);

        await using var db = new BeetwerkDbContext(
            new DbContextOptionsBuilder<BeetwerkDbContext>().UseSqlite($"Data Source={options.DatabasePath}").Options);
        await DataSeeder.MigrateAndSeedAsync(db);
        var users = new UserService(db);

        try
        {
            switch (args[1])
            {
                case "list":
                    foreach (var name in await users.ListAsync())
                        await output.WriteLineAsync(name);
                    return 0;
                case "delete":
                    var deleted = await users.DeleteAsync(args[2]);
                    await output.WriteLineAsync(deleted ? $"Nutzer '{args[2]}' gelöscht." : $"Nutzer '{args[2]}' nicht gefunden.");
                    return deleted ? 0 : 1;
                default:
                    var password = args.Length > 3 ? args[3] : await PromptAsync("Passwort: ", output, input);
                    var created = await users.SetPasswordAsync(args[2], password ?? "");
                    await output.WriteLineAsync(created ? $"Nutzer '{args[2]}' angelegt." : $"Passwort von '{args[2]}' geändert.");
                    return 0;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            await output.WriteLineAsync(ex.Message);
            return 1;
        }
    }

    private static async Task<string?> PromptAsync(string prompt, TextWriter output, TextReader input)
    {
        await output.WriteAsync(prompt);
        return (await input.ReadLineAsync())?.Trim();
    }

    private static async Task<int> HealthCheckAsync(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            return (await client.GetAsync(url)).IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
