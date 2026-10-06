using System.Text.Json.Serialization;
using Kuestencode.Beetwerk.Api.Auth;
using Kuestencode.Beetwerk.Api.Cli;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Endpoints;
using Kuestencode.Beetwerk.Api.Push;
using Kuestencode.Beetwerk.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

if (CommandLine.IsCommand(args))
    return await CommandLine.RunAsync(args, Console.Out, Console.In);

var builder = WebApplication.CreateBuilder(args);
var options = BeetwerkOptions.FromConfiguration(builder.Configuration, builder.Environment.ContentRootPath);
Directory.CreateDirectory(options.DataDirectory);
Directory.CreateDirectory(options.UploadDirectory);
Directory.CreateDirectory(options.KeyDirectory);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<BeetwerkDbContext>(db => db.UseSqlite($"Data Source={options.DatabasePath}"));
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddBeetwerkAuthentication(options);
builder.Services.AddHttpClient(TileEndpoints.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Beetwerk/1.0 (+self-hosted garden planner)");
});

builder.Services.AddSingleton<VapidKeyStore>();
builder.Services.AddSingleton<IPushSender, WebPushSender>();
builder.Services.AddScoped<PushDispatcher>();
builder.Services.AddScoped<TaskNotifier>();
if (options.PushEnabled)
    builder.Services.AddHostedService<TaskNotificationService>();

builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    if (options.TrustForwardedHeaders)
    {
        // Der Reverse Proxy läuft auf dem Host und erreicht den Container über das Docker-Netz, nicht über Loopback.
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
    }
});

var app = builder.Build();

AuthenticationSetup.ValidateAuthConfiguration(options, app.Logger);
if (options.PushRequested && !options.PushEnabled)
    app.Logger.LogWarning("Push ist abgeschaltet, weil PUBLIC_URL kein HTTPS verwendet ({PublicUrl}).", options.PublicUrl);

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BeetwerkDbContext>();
    await DataSeeder.MigrateAndSeedAsync(db);
    var users = scope.ServiceProvider.GetRequiredService<UserService>();
    if (options.AuthMode == AuthMode.None)
        app.Services.GetRequiredService<ImplicitUser>().Id = (await users.EnsureImplicitUserAsync()).Id;
    else if (options.AdminUsername is not null && options.AdminPassword is not null
             && await users.EnsureUserAsync(options.AdminUsername, options.AdminPassword))
        app.Logger.LogInformation("Nutzer '{User}' aus ADMIN_USERNAME angelegt.", options.AdminUsername);
    else if (!(await users.ListAsync()).Any())
        app.Logger.LogWarning("Es gibt noch keinen Nutzer. ADMIN_USERNAME/ADMIN_PASSWORD setzen oder 'user set <name>' ausführen.");
}

if (options.TrustForwardedHeaders)
    app.UseForwardedHeaders();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Statische Dateien liegen hinter der Autorisierung, damit auch die Oberfläche selbst nur angemeldet erreichbar ist.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var headers = context.Context.Response.Headers;
        headers[HeaderNames.CacheControl] = context.Context.Request.Path.StartsWithSegments("/assets")
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    }
});

app.MapGet("/health", async (BeetwerkDbContext db) =>
        await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok", version = options.Version }) : Results.StatusCode(503))
    .AllowAnonymous();
app.MapLoginEndpoints();
app.MapTileEndpoints();

var api = app.MapGroup("/api");
api.MapGet("/me", (System.Security.Claims.ClaimsPrincipal user) => new { Username = user.Identity?.Name });
api.MapGardenEndpoints();
api.MapObjectTypeEndpoints();
api.MapObjectEndpoints();
api.MapSpeciesEndpoints();
api.MapTaskEndpoints();
api.MapPushEndpoints();
api.MapUserEndpoints();
api.MapFallback(() => Results.NotFound());

app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache"
});

await app.RunAsync();
return 0;

public partial class Program;
