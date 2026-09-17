using Microsoft.EntityFrameworkCore;
using Notification.Service.Entities;

namespace Notification.Service.Data;

/// <summary>
/// Notification Service DbContext
/// 
/// Manages:
/// - Notifications (immutable once created, status updates)
/// - NotificationRetries (immutable audit trail of retry attempts)
/// </summary>
public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions<NotificationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRetry> NotificationRetries => Set<NotificationRetry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ========================================================================
        // Notification Entity Configuration
        // ========================================================================
        var notificationBuilder = modelBuilder.Entity<Notification>();

        notificationBuilder.HasKey(n => n.NotificationId);

        // Properties
        notificationBuilder.Property(n => n.NotificationId)
            .ValueGeneratedNever();

        notificationBuilder.Property(n => n.OrderId)
            .IsRequired();

        notificationBuilder.Property(n => n.CustomerId)
            .IsRequired();

        notificationBuilder.Property(n => n.EventType)
            .IsRequired()
            .HasMaxLength(100);

        notificationBuilder.Property(n => n.Subject)
            .IsRequired()
            .HasMaxLength(255);

        notificationBuilder.Property(n => n.Message)
            .IsRequired()
            .HasMaxLength(10000);  // Long message content

        notificationBuilder.Property(n => n.Status)
            .IsRequired()
            .HasConversion<int>();

        notificationBuilder.Property(n => n.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        notificationBuilder.Property(n => n.LastError)
            .HasMaxLength(500);

        notificationBuilder.Property(n => n.MessageId)
            .IsRequired();

        notificationBuilder.Property(n => n.CorrelationId)
            .IsRequired();

        notificationBuilder.Property(n => n.CreatedAt)
            .IsRequired();

        // Indexes
        notificationBuilder.HasIndex(n => n.MessageId)
            .IsUnique();  // Idempotency: MessageId must be unique

        notificationBuilder.HasIndex(n => n.OrderId);

        notificationBuilder.HasIndex(n => n.CustomerId);

        notificationBuilder.HasIndex(n => n.Status);

        notificationBuilder.HasIndex(n => new { n.Status, n.LastAttemptAt })
            .HasDatabaseName("IDX_NotificationStatus_LastAttempt");

        notificationBuilder.HasIndex(n => n.CreatedAt)
            .HasDatabaseName("IDX_NotificationCreatedAt");

        // Check constraints
        notificationBuilder.HasCheckConstraint(
            "CK_Notification_RetryCountRange",
            "\"RetryCount\" >= 0 AND \"RetryCount\" <= 3");

        notificationBuilder.HasCheckConstraint(
            "CK_Notification_CreatedBeforeLastAttempt",
            "\"LastAttemptAt\" IS NULL OR \"CreatedAt\" <= \"LastAttemptAt\"");

        notificationBuilder.HasCheckConstraint(
            "CK_Notification_DeliveredAfterLastAttempt",
            "\"DeliveredAt\" IS NULL OR \"LastAttemptAt\" IS NULL OR \"LastAttemptAt\" <= \"DeliveredAt\"");

        // ========================================================================
        // NotificationRetry Entity Configuration
        // ========================================================================
        var retryBuilder = modelBuilder.Entity<NotificationRetry>();

        retryBuilder.HasKey(r => r.RetryId);

        // Properties
        retryBuilder.Property(r => r.RetryId)
            .ValueGeneratedNever();

        retryBuilder.Property(r => r.NotificationId)
            .IsRequired();

        retryBuilder.Property(r => r.AttemptNumber)
            .IsRequired();

        retryBuilder.Property(r => r.ErrorMessage)
            .HasMaxLength(500);

        retryBuilder.Property(r => r.AttemptedAt)
            .IsRequired();

        retryBuilder.Property(r => r.NextRetryIn)
            .IsRequired();

        // Indexes
        retryBuilder.HasIndex(r => r.NotificationId)
            .HasDatabaseName("IDX_RetryNotificationId");

        retryBuilder.HasIndex(r => r.AttemptedAt)
            .HasDatabaseName("IDX_RetryAttemptedAt");

        // Foreign key
        retryBuilder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(r => r.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Check constraint
        retryBuilder.HasCheckConstraint(
            "CK_NotificationRetry_AttemptNumberRange",
            "\"AttemptNumber\" >= 1 AND \"AttemptNumber\" <= 4");
    }
}
