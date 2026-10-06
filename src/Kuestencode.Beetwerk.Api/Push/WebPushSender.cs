using System.Net;
using System.Text.Json;
using Kuestencode.Beetwerk.Api.Configuration;
using WebPush;

namespace Kuestencode.Beetwerk.Api.Push;

public sealed class WebPushSender(VapidKeyStore vapid, BeetwerkOptions options, ILogger<WebPushSender> logger) : IPushSender, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly WebPushClient client = new();

    public async Task<PushDeliveryResult> SendAsync(Domain.Entities.PushSubscription subscription, PushMessage message, CancellationToken ct)
    {
        var target = new PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
        var details = new VapidDetails(options.VapidSubject, vapid.Keys.PublicKey, vapid.Keys.PrivateKey);
        try
        {
            await client.SendNotificationAsync(target, JsonSerializer.Serialize(message, JsonOptions), details, ct);
            return PushDeliveryResult.Delivered;
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            return PushDeliveryResult.Expired;
        }
        catch (Exception ex) when (ex is WebPushException or HttpRequestException)
        {
            logger.LogWarning(ex, "Push an {Endpoint} fehlgeschlagen.", subscription.Endpoint);
            return PushDeliveryResult.Failed;
        }
    }

    public void Dispose() => client.Dispose();
}
