using Microsoft.EntityFrameworkCore;
using Saga.Orchestrator.Entities;

namespace Saga.Orchestrator.Data;

/// <summary>
/// Saga Orchestrator DbContext
/// 
/// Manages:
/// - SagaState (state machine instances)
/// - SagaEventLog (immutable audit trail)
/// </summary>
public class SagaDbContext : DbContext
{
    public SagaDbContext(DbContextOptions<SagaDbContext> options)
        : base(options)
    {
    }

    public DbSet<SagaState> SagaStates => Set<SagaState>();
    public DbSet<SagaEventLog> SagaEventLogs => Set<SagaEventLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ========================================================================
        // SagaState Entity Configuration
        // ========================================================================
        var sagaBuilder = modelBuilder.Entity<SagaState>();

        sagaBuilder.HasKey(s => s.SagaId);

        // Properties
        sagaBuilder.Property(s => s.SagaId)
            .ValueGeneratedNever();

        sagaBuilder.Property(s => s.OrderId)
            .IsRequired();

        sagaBuilder.Property(s => s.CustomerId)
            .IsRequired();

        sagaBuilder.Property(s => s.OrderAmount)
            .HasPrecision(10, 2)
            .IsRequired();

        sagaBuilder.Property(s => s.Status)
            .IsRequired()
            .HasConversion<int>();

        sagaBuilder.Property(s => s.CurrentStep)
            .HasMaxLength(255);

        sagaBuilder.Property(s => s.CorrelationId)
            .IsRequired();

        sagaBuilder.Property(s => s.FailureReason)
            .HasMaxLength(500);

        sagaBuilder.Property(s => s.LastError)
            .HasMaxLength(500);

        sagaBuilder.Property(s => s.Version)
            .IsConcurrencyToken()
            .ValueGeneratedOnAddOrUpdate();

        // Indexes
        sagaBuilder.HasIndex(s => s.OrderId)
            .IsUnique();  // One saga per order

        sagaBuilder.HasIndex(s => s.Status);

        sagaBuilder.HasIndex(s => s.CreatedAt);

        sagaBuilder.HasIndex(s => new { s.Status, s.TimeoutAt })
            .HasDatabaseName("IDX_SagaStatus_Timeout");

        sagaBuilder.HasIndex(s => s.CorrelationId)
            .HasDatabaseName("IDX_CorrelationId");

        // Check constraints
        sagaBuilder.HasCheckConstraint(
            "CK_Saga_CompensationRetryRange",
            "\"CompensationRetryCount\" >= 0");

        sagaBuilder.HasCheckConstraint(
            "CK_Saga_TimeoutRetryRange",
            "\"TimeoutRetries\" >= 0");

        sagaBuilder.HasCheckConstraint(
            "CK_Saga_TimestampOrdering",
            "\"CompletedAt\" IS NULL OR \"CreatedAt\" <= \"CompletedAt\"");

        // ========================================================================
        // SagaEventLog Entity Configuration
        // ========================================================================
        var eventLogBuilder = modelBuilder.Entity<SagaEventLog>();

        eventLogBuilder.HasKey(e => e.EventId);

        // Properties
        eventLogBuilder.Property(e => e.EventId)
            .ValueGeneratedNever();

        eventLogBuilder.Property(e => e.SagaId)
            .IsRequired();

        eventLogBuilder.Property(e => e.EventType)
            .IsRequired()
            .HasConversion<int>();

        eventLogBuilder.Property(e => e.Details)
            .IsRequired()
            .HasMaxLength(1000);

        eventLogBuilder.Property(e => e.Timestamp)
            .IsRequired();

        eventLogBuilder.Property(e => e.ErrorMessage)
            .HasMaxLength(500);

        // Indexes
        eventLogBuilder.HasIndex(e => e.SagaId)
            .HasDatabaseName("IDX_EventLogSagaId");

        eventLogBuilder.HasIndex(e => e.Timestamp)
            .HasDatabaseName("IDX_EventLogTimestamp");

        eventLogBuilder.HasIndex(e => e.EventType)
            .HasDatabaseName("IDX_EventLogType");

        // Foreign key
        eventLogBuilder.HasOne<SagaState>()
            .WithMany()
            .HasForeignKey(e => e.SagaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
