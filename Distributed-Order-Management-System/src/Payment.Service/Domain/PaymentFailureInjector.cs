namespace Payment.Service.Domain;

/// <summary>
/// Interface for payment failure injection
/// 
/// Purpose: Enable testing saga compensation without external payment API
/// 
/// Use Case:
/// In tests, we want to simulate payment failures to verify compensation:
/// - Payment fails → Inventory should be released
/// - This proves saga compensation works
/// 
/// Without this: Can only test happy path
/// </summary>
public interface IPaymentFailureInjector
{
    /// <summary>
    /// Determine if payment should fail
    /// </summary>
    bool ShouldFail();
}

/// <summary>
/// Configurable payment failure injector for testing
/// 
/// Usage:
/// - Production: Injector.ShouldFail() always returns false
/// - Tests: Injector.ShouldFail() returns true with configured probability
/// - Chaos testing: Injector.ShouldFail() returns true for specific percentages
/// 
/// Example:
///   var injector = new ConfigurablePaymentFailureInjector(failureRate: 0.1);  // 10% failure
///   if (injector.ShouldFail())
///   {
///       // Publish PaymentFailedEvent
///       // Saga will automatically compensate
///   }
/// </summary>
public class ConfigurablePaymentFailureInjector : IPaymentFailureInjector
{
    private readonly double _failureRate;
    private readonly Random _random;

    /// <summary>
    /// Create injector with configurable failure rate
    /// </summary>
    /// <param name="failureRate">Probability of failure (0.0 = never, 1.0 = always), default 0 (never fail)</param>
    /// <param name="seed">Optional seed for reproducible test results</param>
    public ConfigurablePaymentFailureInjector(double failureRate = 0.0, int? seed = null)
    {
        if (failureRate < 0.0 || failureRate > 1.0)
            throw new ArgumentException("FailureRate must be between 0.0 and 1.0", nameof(failureRate));

        _failureRate = failureRate;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Determine if payment should fail
    /// </summary>
    public bool ShouldFail()
    {
        if (_failureRate == 0.0)
            return false;  // Never fail

        if (_failureRate == 1.0)
            return true;   // Always fail

        // Generate random number [0.0, 1.0)
        var randomValue = _random.NextDouble();
        return randomValue < _failureRate;
    }
}

/// <summary>
/// No-op failure injector (always succeeds)
/// </summary>
public class NoOpPaymentFailureInjector : IPaymentFailureInjector
{
    public bool ShouldFail() => false;
}

/// <summary>
/// Always-fail injector (for testing compensation exhaustively)
/// </summary>
public class AlwaysFailPaymentFailureInjector : IPaymentFailureInjector
{
    public bool ShouldFail() => true;
}

