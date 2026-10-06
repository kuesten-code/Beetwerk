using System.Text.Json;
using Kuestencode.Beetwerk.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kuestencode.Beetwerk.Data;

public class BeetwerkDbContext(DbContextOptions<BeetwerkDbContext> options) : DbContext(options)
{
    public DbSet<Garden> Gardens => Set<Garden>();
    public DbSet<ObjectType> ObjectTypes => Set<ObjectType>();
    public DbSet<GardenObject> Objects => Set<GardenObject>();
    public DbSet<PlantSpecies> PlantSpecies => Set<PlantSpecies>();
    public DbSet<NeighborRelation> NeighborRelations => Set<NeighborRelation>();
    public DbSet<GardenTask> Tasks => Set<GardenTask>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite kann DateTimeOffset nicht sortieren/vergleichen; als UTC-Ticks gespeichert bleibt es abfragbar.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Garden>(e =>
        {
            e.Property(g => g.Id).ValueGeneratedNever();
            e.Property(g => g.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<ObjectType>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(100).UseCollation("NOCASE");
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Icon).HasMaxLength(16);
            e.Property(t => t.Color).HasMaxLength(16);
            e.Property(t => t.AllowedGeometries).HasConversion<int>();
            e.Property(t => t.Fields).HasConversion(Json<List<ObjectTypeField>>(), JsonComparer<List<ObjectTypeField>>());
        });

        modelBuilder.Entity<GardenObject>(e =>
        {
            e.Property(o => o.Name).HasMaxLength(200);
            e.Property(o => o.GeometryKind).HasConversion<string>().HasMaxLength(20);
            e.HasOne(o => o.ObjectType).WithMany().HasForeignKey(o => o.ObjectTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.ParentObject).WithMany().HasForeignKey(o => o.ParentObjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(o => o.PlantSpecies).WithMany().HasForeignKey(o => o.PlantSpeciesId).OnDelete(DeleteBehavior.SetNull);
            e.Property(o => o.Attributes).HasConversion(Json<Dictionary<string, string>>(), JsonComparer<Dictionary<string, string>>());
        });

        modelBuilder.Entity<PlantSpecies>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200).UseCollation("NOCASE");
            e.HasIndex(s => s.Name).IsUnique();
            e.Property(s => s.ScientificName).HasMaxLength(200);
            e.Property(s => s.ExternalUrl).HasMaxLength(2000);
        });

        modelBuilder.Entity<NeighborRelation>(e =>
        {
            e.HasOne(r => r.SpeciesA).WithMany().HasForeignKey(r => r.SpeciesAId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.SpeciesB).WithMany().HasForeignKey(r => r.SpeciesBId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.SpeciesAId, r.SpeciesBId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_NeighborRelations_Order", "\"SpeciesAId\" < \"SpeciesBId\""));
            e.Property(r => r.Rating).HasConversion<string>().HasMaxLength(10);
            e.Property(r => r.Source).HasMaxLength(2000);
        });

        modelBuilder.Entity<GardenTask>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(200);
            e.HasOne(t => t.Object).WithMany().HasForeignKey(t => t.ObjectId).OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(t => t.Frequency).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(t => new { t.Status, t.DueDate });
            e.HasIndex(t => t.SeriesId);
            e.Ignore(t => t.Recurrence);
        });

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(100).UseCollation("NOCASE");
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.SecurityStamp).HasMaxLength(64).HasDefaultValue("");
            e.HasMany(u => u.PushSubscriptions).WithOne(s => s.User).HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PushSubscription>(e =>
        {
            e.HasIndex(s => s.Endpoint).IsUnique();
            e.Property(s => s.DeviceLabel).HasMaxLength(200);
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ValueConverter<T, string> Json<T>() where T : new() =>
        new(v => JsonSerializer.Serialize(v, JsonOptions), v => JsonSerializer.Deserialize<T>(v, JsonOptions) ?? new T());

    private static ValueComparer<T> JsonComparer<T>() where T : new() =>
        new(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions) ?? new T());
}
