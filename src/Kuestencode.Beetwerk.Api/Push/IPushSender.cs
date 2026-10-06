namespace Kuestencode.Beetwerk.Api.Push;

public sealed record PushMessage(string Title, string Body, string Url, string Tag);

public enum PushDeliveryResult
{
    Delivered,
    /// <summary>Der Push-Dienst kennt das Abo nicht mehr (404/410); es sollte gelöscht werden.</summary>
    Expired,
    Failed
}

public interface IPushSender
{
    Task<PushDeliveryResult> SendAsync(Domain.Entities.PushSubscription subscription, PushMessage message, CancellationToken ct);
}
