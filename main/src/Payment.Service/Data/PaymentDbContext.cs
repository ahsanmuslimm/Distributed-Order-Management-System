using Microsoft.EntityFrameworkCore;
using Payment.Service.Entities;
using PaymentEntity = Payment.Service.Entities.Payment;

namespace Payment.Service.Data;

/// <summary>
/// Entity Framework Core DbContext for Payment Service
/// 
/// Tables:
/// - Payments: All charge attempts
/// - Refunds: All refunds
/// 
/// Key Design:
/// - Fluent API for configuration
/// - Indexes for common queries
/// - Unique constraints for idempotency
/// - Foreign key constraints
/// - Optimistic concurrency (Version column)
/// </summary>
public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Payments table
    /// </summary>
    public DbSet<PaymentEntity> Payments { get; set; } = null!;

    /// <summary>
    /// Refunds table
    /// </summary>
    public DbSet<Refund> Refunds { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ====================================================================
        // PAYMENTS TABLE
        // ====================================================================

        modelBuilder.Entity<PaymentEntity>(b =>
        {
            // Key
            b.HasKey(p => p.PaymentId);

            // Properties
            b.Property(p => p.PaymentId)
                .ValueGeneratedOnAdd();

            b.Property(p => p.OrderId)
                .IsRequired();

            b.Property(p => p.CustomerId)
                .IsRequired();

            b.Property(p => p.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            b.Property(p => p.Status)
                .HasConversion<int>()
                .IsRequired();

            b.Property(p => p.TransactionId)
                .HasMaxLength(255);

            b.Property(p => p.MessageId)
                .IsRequired();

            b.Property(p => p.CorrelationId)
                .IsRequired();

            b.Property(p => p.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            b.Property(p => p.ProcessedAt)
                .HasColumnType("timestamp with time zone");

            b.Property(p => p.Version)
                .IsRowVersion();

            // Indexes
            b.HasIndex(p => p.MessageId).IsUnique();  // Idempotency
            b.HasIndex(p => p.OrderId);               // Find by order
            b.HasIndex(p => p.CustomerId);            // Find by customer
            b.HasIndex(p => p.Status);                // Find by status
            b.HasIndex(p => p.CorrelationId);         // Tracing
            b.HasIndex(p => p.CreatedAt);             // Time-based queries

            // Constraints
            b.HasCheckConstraint("CK_Payment_Amount_Positive", "\"Amount\" > 0");

            // Navigation
            b.HasMany(p => p.Refunds)
                .WithOne(r => r.Payment)
                .HasForeignKey(r => r.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ====================================================================
        // REFUNDS TABLE
        // ====================================================================

        modelBuilder.Entity<Refund>(b =>
        {
            // Key
            b.HasKey(r => r.RefundId);

            // Properties
            b.Property(r => r.RefundId)
                .ValueGeneratedOnAdd();

            b.Property(r => r.PaymentId)
                .IsRequired();

            b.Property(r => r.Amount)
                .HasPrecision(18, 2)
                .IsRequired();

            b.Property(r => r.Status)
                .HasConversion<int>()
                .IsRequired();

            b.Property(r => r.MessageId)
                .IsRequired();

            b.Property(r => r.CorrelationId)
                .IsRequired();

            b.Property(r => r.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            b.Property(r => r.ProcessedAt)
                .HasColumnType("timestamp with time zone");

            // Indexes
            b.HasIndex(r => r.MessageId).IsUnique();  // Idempotency
            b.HasIndex(r => r.PaymentId);             // Find by payment
            b.HasIndex(r => r.Status);                // Find by status
            b.HasIndex(r => r.CorrelationId);         // Tracing
            b.HasIndex(r => r.CreatedAt);             // Time-based queries

            // Constraints
            b.HasCheckConstraint("CK_Refund_Amount_Positive", "\"Amount\" > 0");

            // Foreign key
            b.HasOne(r => r.Payment)
                .WithMany(p => p.Refunds)
                .HasForeignKey(r => r.PaymentId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
        });

        // ====================================================================
        // CONFIGURATION
        // ====================================================================

        // Use PostgreSQL specific features
        // (Schema, generated columns, etc. if needed in future)
    }
}

