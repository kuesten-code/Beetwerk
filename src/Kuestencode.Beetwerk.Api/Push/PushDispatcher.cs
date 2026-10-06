using Kuestencode.Beetwerk.Data;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Api.Push;

/// <summary>Verteilt eine Nachricht an Abos und räumt dabei abgelaufene Abos auf.</summary>
public class PushDispatcher(BeetwerkDbContext db, IPushSender sender)
{
    public async Task<int> SendAsync(PushMessage message, int? userId, CancellationToken ct)
    {
        var subscriptions = await db.PushSubscriptions
            .Where(s => userId == null || s.UserId == userId)
            .ToListAsync(ct);

        var delivered = 0;
        foreach (var subscription in subscriptions)
        {
            switch (await sender.SendAsync(subscription, message, ct))
            {
                case PushDeliveryResult.Delivered:
                    delivered++;
                    break;
                case PushDeliveryResult.Expired:
                    db.PushSubscriptions.Remove(subscription);
                    break;
            }
        }
        await db.SaveChangesAsync(ct);
        return delivered;
    }
}
