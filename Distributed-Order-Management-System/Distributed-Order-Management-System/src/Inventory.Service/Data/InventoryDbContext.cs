using Microsoft.EntityFrameworkCore;
using Inventory.Service.Entities;

namespace Inventory.Service.Data;

/// <summary>
/// EF Core DbContext for Inventory Service
/// 
/// Design: Append-only ledger pattern
/// - Products: Master data (immutable)
/// - ReservationLedger: Append-only ledger (no updates/deletes)
/// - Current stock = InitialStock - sum(reserves) + sum(releases)
/// </summary>
public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Products table (master data)
    /// </summary>
    public DbSet<Product> Products { get; set; } = null!;

    /// <summary>
    /// Reservation ledger (append-only)
    /// </summary>
    public DbSet<ReservationLedger> ReservationLedgers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ============================================
        // Product Configuration
        // ============================================
        modelBuilder.Entity<Product>(entity =>
        {
            // Primary key
            entity.HasKey(p => p.ProductId);

            // Column mappings
            entity.Property(p => p.ProductId)
                .HasColumnType("uuid")
                .ValueGeneratedNever();

            entity.Property(p => p.Name)
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(p => p.Description)
                .HasMaxLength(1000);

            entity.Property(p => p.Price)
                .HasPrecision(10, 2);

            entity.Property(p => p.InitialStock)
                .HasColumnType("integer");

            entity.Property(p => p.CreatedAt)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Relationships
            entity.HasMany(p => p.ReservationLedgers)
                .WithOne(rl => rl.Product)
                .HasForeignKey(rl => rl.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            entity.HasIndex(p => p.Name)
                .HasName("idx_product_name");

            entity.HasIndex(p => p.CreatedAt)
                .HasName("idx_product_created_at");
        });

        // ============================================
        // ReservationLedger Configuration
        // ============================================
        modelBuilder.Entity<ReservationLedger>(entity =>
        {
            // Primary key
            entity.HasKey(rl => rl.LedgerId);

            // Column mappings
            entity.Property(rl => rl.LedgerId)
                .HasColumnType("uuid")
                .ValueGeneratedNever();

            entity.Property(rl => rl.ProductId)
                .HasColumnType("uuid");

            entity.Property(rl => rl.OrderId)
                .HasColumnType("uuid");

            entity.Property(rl => rl.Quantity)
                .HasColumnType("integer");

            entity.Property(rl => rl.Type)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(rl => rl.MessageId)
                .HasColumnType("uuid");

            entity.Property(rl => rl.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(rl => rl.CreatedAt)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(rl => rl.ProcessedAt)
                .HasColumnType("timestamptz");

            entity.Property(rl => rl.CorrelationId)
                .HasColumnType("uuid");

            // Constraints
            entity.HasCheckConstraint("chk_ledger_quantity_positive", "[Quantity] > 0");

            // Indexes
            entity.HasIndex(rl => rl.ProductId)
                .HasName("idx_ledger_product_id");

            entity.HasIndex(rl => rl.OrderId)
                .HasName("idx_ledger_order_id");

            entity.HasIndex(rl => rl.MessageId)
                .IsUnique()
                .HasName("idx_ledger_message_id_unique");

            entity.HasIndex(rl => rl.Type)
                .HasName("idx_ledger_type");

            entity.HasIndex(rl => rl.Status)
                .HasName("idx_ledger_status");

            entity.HasIndex(rl => rl.CreatedAt)
                .HasName("idx_ledger_created_at");

            entity.HasIndex(rl => rl.CorrelationId)
                .HasName("idx_ledger_correlation_id");

            // Composite index for stock calculation
            entity.HasIndex(rl => new { rl.ProductId, rl.Type })
                .HasName("idx_ledger_product_type");
        });
    }
}
