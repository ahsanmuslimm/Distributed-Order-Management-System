using Microsoft.EntityFrameworkCore;
using Orders.Service.Entities;

namespace Orders.Service.Data;

/// <summary>
/// EF Core DbContext for Order Service
/// 
/// Configuration:
/// - Fluent API for constraints, indexes, and concurrency control
/// - Optimistic concurrency via Version property
/// - Correlation ID and CorrelationId propagation via shadow properties
/// - Timestamp automation via default value expressions
/// </summary>
public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Orders table
    /// </summary>
    public DbSet<Order> Orders { get; set; } = null!;

    /// <summary>
    /// Order items (line items)
    /// </summary>
    public DbSet<OrderItem> OrderItems { get; set; } = null!;

    /// <summary>
    /// Status transition audit trail
    /// </summary>
    public DbSet<OrderStatusTransition> OrderStatusTransitions { get; set; } = null!;

    /// <summary>
    /// Inbox messages for idempotency (Inbox Pattern)
    /// </summary>
    public DbSet<InboxMessage> InboxMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ============================================
        // Order Configuration
        // ============================================
        modelBuilder.Entity<Order>(entity =>
        {
            // Primary key
            entity.HasKey(o => o.OrderId);

            // Column mappings
            entity.Property(o => o.OrderId)
                .HasColumnType("uuid")
                .ValueGeneratedNever();

            entity.Property(o => o.CustomerId)
                .HasColumnType("uuid");

            entity.Property(o => o.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(o => o.TotalAmount)
                .HasPrecision(10, 2);

            entity.Property(o => o.SagaId)
                .HasColumnType("uuid");

            entity.Property(o => o.CreatedAt)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(o => o.LastUpdatedAt)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Concurrency control
            entity.Property(o => o.Version)
                .IsConcurrencyToken();

            // Relationships
            entity.HasMany(o => o.Items)
                .WithOne(oi => oi.Order)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(o => o.StatusTransitions)
                .WithOne(st => st.Order)
                .HasForeignKey(st => st.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(o => o.CustomerId)
                .HasName("idx_order_customer_id");

            entity.HasIndex(o => o.Status)
                .HasName("idx_order_status");

            entity.HasIndex(o => o.CreatedAt)
                .HasName("idx_order_created_at");

            entity.HasIndex(o => o.SagaId)
                .HasName("idx_order_saga_id");
        });

        // ============================================
        // OrderItem Configuration
        // ============================================
        modelBuilder.Entity<OrderItem>(entity =>
        {
            // Primary key
            entity.HasKey(oi => oi.OrderItemId);

            // Column mappings
            entity.Property(oi => oi.OrderItemId)
                .HasColumnType("uuid")
                .ValueGeneratedNever();

            entity.Property(oi => oi.OrderId)
                .HasColumnType("uuid");

            entity.Property(oi => oi.ProductId)
                .HasColumnType("uuid");

            entity.Property(oi => oi.Quantity)
                .HasColumnType("integer");

            entity.Property(oi => oi.UnitPrice)
                .HasPrecision(10, 2);

            // Constraints
            entity.HasCheckConstraint("chk_order_item_quantity_positive", "[Quantity] > 0");
        });

        // ============================================
        // OrderStatusTransition Configuration
        // ============================================
        modelBuilder.Entity<OrderStatusTransition>(entity =>
        {
            // Primary key
            entity.HasKey(st => st.TransitionId);

            // Column mappings
            entity.Property(st => st.TransitionId)
                .HasColumnType("uuid")
                .ValueGeneratedNever();

            entity.Property(st => st.OrderId)
                .HasColumnType("uuid");

            entity.Property(st => st.FromStatus)
                .HasConversion<string?>()
                .HasMaxLength(50);

            entity.Property(st => st.ToStatus)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(st => st.Reason)
                .HasMaxLength(255);

            entity.Property(st => st.Timestamp)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(st => st.CorrelationId)
                .HasColumnType("uuid");

            // Indexes
            entity.HasIndex(st => st.OrderId)
                .HasName("idx_transition_order_id");

            entity.HasIndex(st => st.Timestamp)
                .HasName("idx_transition_timestamp");

            entity.HasIndex(st => st.CorrelationId)
                .HasName("idx_transition_correlation_id");
        });

        // ============================================
        // InboxMessage Configuration
        // ============================================
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            // Primary key
            entity.HasKey(im => im.MessageId);

            // Column mappings
            entity.Property(im => im.MessageId)
                .HasColumnType("uuid")
                .ValueGeneratedNever();

            entity.Property(im => im.OrderId)
                .HasColumnType("uuid");

            entity.Property(im => im.MessageType)
                .HasMaxLength(100);

            entity.Property(im => im.Payload)
                .HasColumnType("text");

            entity.Property(im => im.ProcessedAt)
                .HasColumnType("timestamptz")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(im => im.CorrelationId)
                .HasColumnType("uuid");

            // Constraints
            entity.HasIndex(im => im.MessageId)
                .IsUnique()
                .HasName("idx_inbox_message_id_unique");

            entity.HasIndex(im => im.OrderId)
                .HasName("idx_inbox_order_id");

            entity.HasIndex(im => im.CorrelationId)
                .HasName("idx_inbox_correlation_id");
        });
    }
}
