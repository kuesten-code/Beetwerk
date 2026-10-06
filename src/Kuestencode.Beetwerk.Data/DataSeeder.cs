using Kuestencode.Beetwerk.Domain.Entities;
using Kuestencode.Beetwerk.Domain.Seed;
using Microsoft.EntityFrameworkCore;

namespace Kuestencode.Beetwerk.Data;

public static class DataSeeder
{
    public static async Task MigrateAndSeedAsync(BeetwerkDbContext db, CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.ObjectTypes.AnyAsync(ct))
            db.ObjectTypes.AddRange(DefaultObjectTypes.Create());

        if (!await db.Gardens.AnyAsync(ct))
            db.Gardens.Add(new Garden { CenterLatitude = 54.32, CenterLongitude = 10.13, Zoom = 17 });

        await db.SaveChangesAsync(ct);
    }
}
