using Microsoft.EntityFrameworkCore;
using SteamAnaliticWorker.Models;

namespace SteamAnaliticWorker.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Item> Items { get; set; }
    public DbSet<BuyOrderRecord> BuyOrders { get; set; }
    public DbSet<SellOrderRecord> SellOrders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ItemId).IsUnique();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.ItemId).IsRequired();
        });

        modelBuilder.Entity<BuyOrderRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ItemId, e.Price });
            entity.HasIndex(e => new { e.ItemId, e.CollectedAt });
            entity.HasOne(e => e.Item)
                .WithMany()
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SellOrderRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ItemId, e.Price });
            entity.HasIndex(e => new { e.ItemId, e.CollectedAt });
            entity.HasOne(e => e.Item)
                .WithMany()
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

