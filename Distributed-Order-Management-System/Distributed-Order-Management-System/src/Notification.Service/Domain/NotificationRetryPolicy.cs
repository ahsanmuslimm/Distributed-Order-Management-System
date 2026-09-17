namespace Notification.Service.Domain;

/// <summary>
/// Notification Retry Policy
/// 
/// Implements exponential backoff for delivery retries.
/// 
/// Retry Schedule:
/// - Attempt 1: Immediate
/// - Attempt 2: After 1 second (if first fails)
/// - Attempt 3: After 2 seconds (if second fails)
/// - Attempt 4: After 4 seconds (if third fails)
/// - After 4 attempts: Send to DLQ
/// 
/// Formula: nextRetry = initialBackoff * (multiplier ^ attempt)
///          capped at maxBackoff
/// 
/// Configuration:
/// - InitialBackoff: 1 second
/// - Multiplier: 2.0 (doubles each attempt)
/// - MaxBackoff: 30 seconds (cap)
/// - MaxRetries: 3 (4 total attempts: 1 immediate + 3 retries)
/// 
/// Why exponential backoff?
/// - Prevents thundering herd (all notifications retry at once)
/// - Allows transient failures to recover (network blips, service restart)
/// - Prevents cascading failures (backing off gives system time)
/// </summary>
public interface INotificationRetryPolicy
{
    /// <summary>
    /// Calculate delay for next retry
    /// </summary>
    TimeSpan CalculateBackoff(int attemptNumber);

    /// <summary>
    /// Check if should retry (attempt <= max retries)
    /// </summary>
    bool ShouldRetry(int attemptNumber);

    /// <summary>
    /// Maximum retry attempts
    /// </summary>
    int MaxRetries { get; }
}

/// <summary>
/// Default implementation with exponential backoff
/// </summary>
public class ExponentialBackoffRetryPolicy : INotificationRetryPolicy
{
    private readonly TimeSpan _initialBackoff;
    private readonly double _multiplier;
    private readonly TimeSpan _maxBackoff;
    private readonly int _maxRetries;

    /// <summary>
    /// Create with defaults:
    /// - InitialBackoff: 1 second
    /// - Multiplier: 2.0
    /// - MaxBackoff: 30 seconds
    /// - MaxRetries: 3
    /// </summary>
    public ExponentialBackoffRetryPolicy()
        : this(
            initialBackoff: TimeSpan.FromSeconds(1),
            multiplier: 2.0,
            maxBackoff: TimeSpan.FromSeconds(30),
            maxRetries: 3)
    {
    }

    /// <summary>
    /// Create with custom configuration
    /// </summary>
    public ExponentialBackoffRetryPolicy(
        TimeSpan initialBackoff,
        double multiplier,
        TimeSpan maxBackoff,
        int maxRetries)
    {
        if (initialBackoff <= TimeSpan.Zero)
            throw new ArgumentException("InitialBackoff must be positive", nameof(initialBackoff));

        if (multiplier <= 1.0)
            throw new ArgumentException("Multiplier must be > 1.0", nameof(multiplier));

        if (maxBackoff <= TimeSpan.Zero)
            throw new ArgumentException("MaxBackoff must be positive", nameof(maxBackoff));

        if (maxRetries < 0)
            throw new ArgumentException("MaxRetries must be >= 0", nameof(maxRetries));

        _initialBackoff = initialBackoff;
        _multiplier = multiplier;
        _maxBackoff = maxBackoff;
        _maxRetries = maxRetries;
    }

    public int MaxRetries => _maxRetries;

    /// <summary>
    /// Calculate backoff for given attempt
    /// 
    /// Formula: backoff = initialBackoff * (multiplier ^ attemptNumber)
    ///          capped at maxBackoff
    /// </summary>
    public TimeSpan CalculateBackoff(int attemptNumber)
    {
        if (attemptNumber < 1)
            throw new ArgumentException("Attempt number must be >= 1", nameof(attemptNumber));

        // Calculate: initialBackoff * (multiplier ^ attemptNumber)
        var exponentialBackoff = _initialBackoff.TotalSeconds * Math.Pow(_multiplier, attemptNumber - 1);

        // Cap at maxBackoff
        var backoffSeconds = Math.Min(exponentialBackoff, _maxBackoff.TotalSeconds);

        return TimeSpan.FromSeconds(backoffSeconds);
    }

    /// <summary>
    /// Check if should retry (attemptNumber <= maxRetries)
    /// </summary>
    public bool ShouldRetry(int attemptNumber)
    {
        return attemptNumber <= _maxRetries;
    }
}

/// <summary>
/// No-retry policy (for testing)
/// </summary>
public class NoRetryPolicy : INotificationRetryPolicy
{
    public int MaxRetries => 0;

    public TimeSpan CalculateBackoff(int attemptNumber)
    {
        return TimeSpan.Zero;
    }

    public bool ShouldRetry(int attemptNumber)
    {
        return false;
    }
}

/// <summary>
/// Immediate-retry policy (for testing)
/// </summary>
public class ImmediateRetryPolicy : INotificationRetryPolicy
{
    private readonly int _maxRetries;

    public ImmediateRetryPolicy(int maxRetries = 3)
    {
        _maxRetries = maxRetries;
    }

    public int MaxRetries => _maxRetries;

    public TimeSpan CalculateBackoff(int attemptNumber)
    {
        return TimeSpan.Zero;  // No delay
    }

    public bool ShouldRetry(int attemptNumber)
    {
        return attemptNumber <= _maxRetries;
    }
}

/// <summary>
/// Extension methods for retry policy
/// </summary>
public static class RetryPolicyExtensions
{
    /// <summary>
    /// Get next retry time
    /// </summary>
    public static DateTime GetNextRetryTime(
        this INotificationRetryPolicy policy,
        DateTime lastAttemptTime,
        int nextAttemptNumber)
    {
        var backoff = policy.CalculateBackoff(nextAttemptNumber);
        return lastAttemptTime.Add(backoff);
    }

    /// <summary>
    /// Check if notification should be retried
    /// </summary>
    public static bool ShouldRetry(
        this INotificationRetryPolicy policy,
        int currentRetryCount,
        DateTime? lastAttemptTime)
    {
        if (!policy.ShouldRetry(currentRetryCount + 1))
            return false;

        if (!lastAttemptTime.HasValue)
            return true;

        // Check if enough time has passed
        var backoff = policy.CalculateBackoff(currentRetryCount + 1);
        var nextRetryTime = lastAttemptTime.Value.Add(backoff);

        return DateTime.UtcNow >= nextRetryTime;
    }
}
