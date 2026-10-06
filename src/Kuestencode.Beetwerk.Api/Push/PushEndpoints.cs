using System.Security.Claims;
using Kuestencode.Beetwerk.Api.Auth;
using Kuestencode.Beetwerk.Api.Configuration;
using Kuestencode.Beetwerk.Api.Contracts;
using Kuestencode.Beetwerk.Data;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Push;

public static class PushEndpoints
{
    public static void MapPushEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/push");

        group.MapPost("/subscriptions", async (PushSubscriptionInput input, ClaimsPrincipal user, BeetwerkDbContext db,
            BeetwerkOptions options, TimeProvider time, CancellationToken ct) =>
        {
            if (!options.PushEnabled)
                return ApiResults.Conflict("Push ist auf diesem Server deaktiviert.");
            if (!Uri.TryCreate(input.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
                return ApiResults.Error("Ungültiger Push-Endpunkt.");
            if (string.IsNullOrWhiteSpace(input.Keys?.P256dh) || string.IsNullOrWhiteSpace(input.Keys.Auth))
                return ApiResults.Error("Schlüssel des Push-Abos fehlen.");

            var subscription = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == input.Endpoint, ct);
            if (subscription is null)
            {
                subscription = new Domain.Entities.PushSubscription
                {
                    Endpoint = input.Endpoint, P256dh = "", Auth = "", CreatedAt = time.GetUtcNow()
                };
                db.PushSubscriptions.Add(subscription);
            }
            subscription.UserId = user.UserId();
            subscription.P256dh = input.Keys.P256dh;
            subscription.Auth = input.Keys.Auth;
            var label = input.DeviceLabel?.Trim();
            subscription.DeviceLabel = string.IsNullOrEmpty(label) ? null : label[..Math.Min(label.Length, 200)];
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapDelete("/subscriptions", async (string endpoint, ClaimsPrincipal user, BeetwerkDbContext db, CancellationToken ct) =>
        {
            var userId = user.UserId();
            await db.PushSubscriptions.Where(s => s.Endpoint == endpoint && s.UserId == userId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });

        group.MapPost("/test", async (ClaimsPrincipal user, PushDispatcher dispatcher, BeetwerkOptions options, CancellationToken ct) =>
        {
            if (!options.PushEnabled)
                return ApiResults.Conflict("Push ist auf diesem Server deaktiviert.");
            var delivered = await dispatcher.SendAsync(
                new PushMessage("Beetwerk", "Testbenachrichtigung – Push funktioniert. 🌱", "/aufgaben", "test"), user.UserId(), ct);
            return Results.Ok(new { delivered });
        });
    }
}
