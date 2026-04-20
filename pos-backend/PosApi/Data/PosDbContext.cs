using Microsoft.EntityFrameworkCore;
using PosApi.Models;

namespace PosApi.Data;

public class PosDbContext(DbContextOptions<PosDbContext> options) : DbContext(options)
{
    public DbSet<Tab> Tabs => Set<Tab>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tab>(e =>
        {
            e.HasKey(t => t.TabId);
            e.Property(t => t.GrandTotal).HasPrecision(18, 4);
            e.HasMany(t => t.Payments)
             .WithOne(p => p.Tab)
             .HasForeignKey(p => p.TabId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.HasKey(p => p.PaymentId);
            e.Property(p => p.Amount).HasPrecision(18, 4);
        });
    }
}
