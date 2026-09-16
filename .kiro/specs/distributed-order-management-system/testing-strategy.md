# Testing Strategy - Distributed Order Management System

## Executive Summary

Testing drives this project. We use **property-based testing** to define correctness (not just verify it), and **integration testing** with **Testcontainers** to validate behavior with real infrastructure.

**Testing Pyramid**:
```
           Integration Tests (8 scenarios, real Kafka/Postgres/Redis)
          /      End-to-end saga flows, chaos scenarios      \
         /________________________________________________\
        /
       /    Property-Based Tests (50+ properties, FsCheck/QuickCheck)
      /      Random input generation, invariant verification      \
     /____________________________________________________\
    /
   /       Unit Tests (handlers, business logic, pure functions)
  /________________________________________________________\
```

**Coverage Target**: Every requirement from requirements.md maps to a test; every test passes before code ships.

---

## Part 1: Property-Based Testing

### What is a Property?

A property is a **mathematical invariant** that must hold for all valid inputs.

**Example**:
```
Property: Idempotent Reservation

∀ reserveCmd ∈ ReserveInventoryCommands:
  ∀ messageId ∈ UniqueMessageIds:
    processReservation(cmd, messageId)
    processReservation(cmd, messageId)  // Replay with same messageId
    ≡ processReservation(cmd, messageId) // Result identical to first

Interpretation: Processing the same reserve command twice with the same message ID produces identical state.
```

### Why Property-Based Testing?

**Example Failure (without property-based testing)**:
```csharp
// Unit test (traditional)
[Test]
public void ReserveInventory_WithDuplicateMessageId_Suppresses_Duplicate()
{
    // Arrange
    var cmd = new ReserveInventoryCommand { 
        ProductId = Guid.Parse("12345678..."),  // Fixed UUID
        Quantity = 5,
        MessageId = Guid.Parse("87654321...")   // Fixed UUID
    };
    
    // Act & Assert
    _handler.Handle(cmd);
    _handler.Handle(cmd);
    
    var stock = _db.Products.First(p => p.ProductId == Guid.Parse("12345678...")).Stock;
    Assert.AreEqual(stock, 995); // Assumes initial stock = 1000
}

// Problem: This test only covers ONE specific combination of inputs.
// It doesn't find edge cases:
// - Quantity = 0? Negative? Huge?
// - MessageId collision patterns?
// - Stock exactly matching quantity?
// - Concurrent duplicate messages milliseconds apart?
```

**Property-Based Testing Solution**:
```csharp
[Property]
public void ReserveInventory_WithDuplicateMessageId_IsIdempotent(
    Guid productId,
    Guid orderId,
    int quantity,
    Guid messageId)
{
    Assume.That(quantity, Is.GreaterThan(0).And.LessThanOrEqualTo(10000));
    
    var initialStock = _db.SetProductStock(productId, 10000);
    
    var cmd = new ReserveInventoryCommand 
    { 
        ProductId = productId,
        OrderId = orderId,
        Quantity = quantity,
        MessageId = messageId
    };
    
    // Act: FsCheck generates 100+ random combinations of above parameters
    var result1 = _handler.Handle(cmd);
    var stock1 = _db.GetProductStock(productId);
    
    var result2 = _handler.Handle(cmd); // Replay
    var stock2 = _db.GetProductStock(productId);
    
    // Assert: Property holds for ALL generated inputs
    Assert.AreEqual(result1.IsSuccess, result2.IsSuccess);
    Assert.AreEqual(stock1, stock2);
}

// Benefit: Automatically tests hundreds of edge cases.
// FsCheck will try:
// - Quantity = 10000 (full stock), 0 (edge), 1, 5, random values
// - MessageId = new Guid(), repeated, sequential patterns
// - ProductId = various UUIDs
// - Order of operations varies
```

### FsCheck Setup (C# Property-Based Testing)

**NuGet Package**:
```bash
dotnet add package FsCheck.Xunit
dotnet add package FsCheck.Generators
```

**Basic Example**:
```csharp
using FsCheck;
using FsCheck.Xunit;

[Property]
public void Example_Property_AllIntsHaveToString(int x)
{
    // Property: All integers can be converted to string
    var result = x.ToString();
    
    // Assert: Result is not null
    Assert.NotNull(result);
}
```

**For This Project**:
```csharp
// Custom Arbitrary generator for domain objects
public class ReserveInventoryCommandArbitrary
{
    public static Arbitrary<ReserveInventoryCommand> Commands() =>
        from productId in Arb.Default.Guid()
        from orderId in Arb.Default.Guid()
        from quantity in Gen.Choose(1, 10000).ToArbitrary()
        from messageId in Arb.Default.Guid()
        select new ReserveInventoryCommand
        {
            ProductId = productId,
            OrderId = orderId,
            Quantity = quantity,
            MessageId = messageId
        };
}

// Test using custom generator
[Property(Arbitrary = new[] { typeof(ReserveInventoryCommandArbitrary) })]
public void ReserveInventory_AllCommands_ProduceValidResults(
    ReserveInventoryCommand cmd)
{
    var result = _handler.Handle(cmd);
    
    // Property: Result is always valid
    Assert.That(result.IsSuccess || result.ErrorMessage != null);
}
```

### Categories of Properties

#### 1. Idempotency Properties

**Pattern**: Executing operation twice ≡ executing once

**Example**: Property 2.1.1 (Inventory Idempotency)
```csharp
[Property]
public void ReserveInventory_DuplicateMessageId_Idempotent(
    ReserveInventoryCommand cmd,
    Guid messageId)
{
    var result1 = ProcessWithInbox(cmd, messageId);
    var result2 = ProcessWithInbox(cmd, messageId); // Same messageId
    
    Assert.Equal(result1.State, result2.State);
}
```

#### 2. Consistency Properties

**Pattern**: State transitions preserve invariants

**Example**: Property 2.1.5 (Stock Never Negative)
```csharp
[Property]
public void ReserveInventory_Stock_NeverNegative(
    List<ReserveInventoryCommand> commands)
{
    foreach (var cmd in commands)
    {
        try { _handler.Handle(cmd); } catch { }
    }
    
    var stock = _db.GetProductStock(cmd.ProductId);
    Assert.That(stock, Is.GreaterThanOrEqualTo(0));
}
```

#### 3. Round-Trip Properties

**Pattern**: Inverse operations cancel out

**Example**: Property 2.2.2 (Reserve-Release Round Trip)
```csharp
[Property]
public void ReserveAndRelease_RoundTrip_RestoresInitialStock(
    Guid productId,
    int quantity)
{
    Assume.That(quantity, Is.GreaterThan(0).And.LessThanOrEqualTo(1000));
    
    var initialStock = _db.SetProductStock(productId, 500);
    
    var reserve = new ReserveInventoryCommand { ProductId = productId, Quantity = quantity, ... };
    var release = new ReleaseInventoryCommand { ProductId = productId, Quantity = quantity, ... };
    
    _handler.Handle(reserve);
    _handler.Handle(release);
    
    var finalStock = _db.GetProductStock(productId);
    Assert.Equal(finalStock, initialStock);
}
```

#### 4. Monotonicity Properties

**Pattern**: Value increases/decreases monotonically

**Example**: Property 4.2.2 (Retry Count Monotonicity)
```csharp
[Property]
public void Notification_RetryCount_Increases(Notification notif)
{
    var initialCount = notif.RetryCount;
    
    _retryEngine.AttemptDelivery(notif); // This may increment retry
    
    var finalCount = notif.RetryCount;
    Assert.That(finalCount, Is.GreaterThanOrEqualTo(initialCount));
}
```

#### 5. Invariant Properties

**Pattern**: Constraint always holds

**Example**: Property 8.3.3 (Error Metrics Classified)
```csharp
[Property]
public void ErrorMetric_HasValidErrorType(ErrorMetric metric)
{
    var validTypes = new[] { "TimeoutError", "ValidationError", "PaymentError", "NetworkError" };
    Assert.That(metric.ErrorType, Is.AnyOf(validTypes));
}
```

### Test Organization

```
src/
  Orders.Service/
  
tests/
  Orders.Service.Tests/
    OrderServicePropertyTests.cs          ← All Property 1.1.x, 1.2.x, 1.3.x
    OrderServiceIntegrationTests.cs       ← Integration with Postgres via Testcontainers
    OrderServiceUnitTests.cs              ← Handler logic, business rules
    
  Inventory.Service.Tests/
    InventoryPropertyTests.cs             ← All Property 2.1.x, 2.2.x, 2.3.x
    InventoryIntegrationTests.cs          ← Real Postgres + Redis
    
  Payment.Service.Tests/
    PaymentPropertyTests.cs               ← All Property 3.1.x, 3.2.x
    
  Saga.Orchestrator.Tests/
    SagaPropertyTests.cs                  ← All Property 5.1.x, 5.2.x, 5.3.x
    SagaCompensationPropertyTests.cs      ← CRITICAL: Property 5.2.1–5.2.5
    
  Kafka.Tests/
    KafkaLayerPropertyTests.cs            ← All Property 6.1.x–6.4.x
    
  Integration.Tests/
    EndToEndSagaTests.cs                  ← Scenario-based integration
    ChaosTests.cs                         ← Failure injection
```

---

## Part 2: Integration Testing with Testcontainers

### What is Testcontainers?

**Definition**: Spin up Docker containers (PostgreSQL, Kafka, Redis) for each test; container lifecycle automatically managed.

**Benefit**: Tests run against **real infrastructure**, not mocks.

**Example**:
```csharp
using Testcontainers.PostgreSql;
using Testcontainers.Kafka;

[TestFixture]
public class OrderServiceIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres;
    private KafkaContainer _kafka;
    private OrderDbContext _dbContext;
    private KafkaProducer _producer;
    
    // Called once before all tests in this class
    async Task IAsyncLifetime.InitializeAsync()
    {
        // Spin up containers
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:15")
            .Build();
        
        _kafka = new KafkaBuilder()
            .Build();
        
        await _postgres.StartAsync();
        await _kafka.StartAsync();
        
        // Create DbContext with container connection string
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        
        _dbContext = new OrderDbContext(options);
        await _dbContext.Database.MigrateAsync();
        
        // Create Kafka producer with container broker address
        _producer = new KafkaProducer(_kafka.GetBootstrapAddress());
    }
    
    // Called once after all tests
    async Task IAsyncLifetime.DisposeAsync()
    {
        _dbContext?.Dispose();
        await _postgres?.StopAsync();
        await _kafka?.StopAsync();
    }
    
    [Test]
    public async Task PlaceOrder_PublishesEvent_To_Kafka()
    {
        // Arrange
        var cmd = new PlaceOrderCommand { OrderId = Guid.NewGuid(), ... };
        
        // Act
        var result = await _handler.Handle(cmd);
        
        // Assert
        var events = await _producer.ConsumeAsync("order.events", timeout: 5s);
        Assert.That(events, Has.Count.GreaterThan(0));
        Assert.That(events[0].Type, Is.EqualTo(typeof(OrderPlacedEvent)));
    }
}
```

### Testcontainers Setup

**NuGet Packages**:
```bash
dotnet add package Testcontainers
dotnet add package Testcontainers.PostgreSql
dotnet add package Testcontainers.Kafka
dotnet add package Testcontainers.Redis
```

**For This Project** (Real Infrastructure Stack):
```csharp
public abstract class IntegrationTestBase : IAsyncLifetime
{
    // Shared containers for all tests
    protected PostgreSqlContainer PostgresOrderDb { get; private set; }
    protected PostgreSqlContainer PostgresInventoryDb { get; private set; }
    protected PostgreSqlContainer PostgresPaymentDb { get; private set; }
    protected PostgreSqlContainer PostgresNotificationDb { get; private set; }
    protected PostgreSqlContainer PostgresSagaDb { get; private set; }
    protected KafkaContainer Kafka { get; private set; }
    protected RedisContainer Redis { get; private set; }
    
    protected OrderDbContext OrderDbContext { get; private set; }
    protected InventoryDbContext InventoryDbContext { get; private set; }
    protected PaymentDbContext PaymentDbContext { get; private set; }
    protected NotificationDbContext NotificationDbContext { get; private set; }
    protected SagaDbContext SagaDbContext { get; private set; }
    
    protected IProducerFactory ProducerFactory { get; private set; }
    protected IConsumerFactory ConsumerFactory { get; private set; }
    protected IConnectionMultiplexer Redis { get; private set; }
    
    async Task IAsyncLifetime.InitializeAsync()
    {
        // Start all containers
        PostgresOrderDb = new PostgreSqlBuilder()
            .WithDatabase("orders")
            .Build();
        // ... repeat for other Postgres instances
        
        Kafka = new KafkaBuilder().Build();
        Redis = new RedisBuilder().Build();
        
        await Task.WhenAll(
            PostgresOrderDb.StartAsync(),
            PostgresInventoryDb.StartAsync(),
            PostgresPaymentDb.StartAsync(),
            PostgresNotificationDb.StartAsync(),
            PostgresSagaDb.StartAsync(),
            Kafka.StartAsync(),
            Redis.StartAsync()
        );
        
        // Initialize DbContexts
        OrderDbContext = new OrderDbContext(new DbContextOptionsBuilder()
            .UseNpgsql(PostgresOrderDb.GetConnectionString()).Options);
        
        // ... repeat for other DbContexts
        
        // Run migrations
        await Task.WhenAll(
            OrderDbContext.Database.MigrateAsync(),
            InventoryDbContext.Database.MigrateAsync(),
            // ... etc
        );
        
        // Initialize factories
        ProducerFactory = new KafkaProducerFactory(Kafka.GetBootstrapAddress());
        ConsumerFactory = new KafkaConsumerFactory(Kafka.GetBootstrapAddress());
    }
    
    async Task IAsyncLifetime.DisposeAsync()
    {
        // Containers auto-cleanup
        await Task.WhenAll(
            PostgresOrderDb.StopAsync(),
            PostgresInventoryDb.StopAsync(),
            // ... etc
            Kafka.StopAsync(),
            Redis.StopAsync()
        );
    }
}
```

### Integration Test Scenarios

#### Scenario 1: Happy Path (Order → Reserve → Charge → Complete)

```csharp
[Test]
public async Task HappyPath_Order_Completes_Successfully()
{
    // Arrange
    var customerId = Guid.NewGuid();
    var productId = Guid.NewGuid();
    var correlationId = Guid.NewGuid();
    
    InventoryDbContext.Products.Add(new Product { ProductId = productId, Stock = 100 });
    await InventoryDbContext.SaveChangesAsync();
    
    // Act: Place order
    var orderResponse = await _orderService.PlaceOrder(new PlaceOrderRequest
    {
        CustomerId = customerId,
        Items = new[] { new OrderItem { ProductId = productId, Quantity = 5 } }
    }, correlationId);
    
    // Assert: Order created
    Assert.That(orderResponse.Status, Is.EqualTo("Pending"));
    var orderId = orderResponse.OrderId;
    
    // Simulate saga progression
    await _sagaOrchestrator.HandleOrderPlacedEvent(new OrderPlacedEvent 
    { 
        OrderId = orderId, 
        CorrelationId = correlationId 
    });
    
    // Wait for saga to complete
    await Task.Delay(1000);
    
    // Assert: Saga reached completion
    var sagaState = await SagaDbContext.SagaStates.FirstAsync(s => s.OrderId == orderId);
    Assert.That(sagaState.CurrentState, Is.EqualTo("Completed"));
    
    // Assert: Order marked confirmed
    var order = await OrderDbContext.Orders.FirstAsync(o => o.OrderId == orderId);
    Assert.That(order.Status, Is.EqualTo("Confirmed"));
    
    // Assert: Stock decremented
    var product = await InventoryDbContext.Products.FirstAsync(p => p.ProductId == productId);
    Assert.That(product.Stock, Is.EqualTo(95)); // 100 - 5
    
    // Assert: Notification sent
    var notification = await NotificationDbContext.Notifications
        .FirstAsync(n => n.OrderId == orderId && n.EventType == "OrderConfirmed");
    Assert.That(notification.Status, Is.EqualTo("Delivered"));
}
```

#### Scenario 2: 🔴 CRITICAL - Payment Failure Compensation

```csharp
[Test]
public async Task PaymentFailure_Triggers_Compensation()
{
    // Arrange
    var customerId = Guid.NewGuid();
    var productId = Guid.NewGuid();
    var correlationId = Guid.NewGuid();
    
    InventoryDbContext.Products.Add(new Product { ProductId = productId, Stock = 100 });
    await InventoryDbContext.SaveChangesAsync();
    
    // Force payment to fail
    _paymentFailureInjector.SetFailureRate(1.0);
    
    // Act: Place order
    var orderResponse = await _orderService.PlaceOrder(new PlaceOrderRequest
    {
        CustomerId = customerId,
        Items = new[] { new OrderItem { ProductId = productId, Quantity = 5 } }
    }, correlationId);
    
    var orderId = orderResponse.OrderId;
    
    // Simulate saga progression
    await _sagaOrchestrator.HandleOrderPlacedEvent(new OrderPlacedEvent { OrderId = orderId, CorrelationId = correlationId });
    
    // Wait for saga to progress through charge attempt and fail
    await Task.Delay(2000);
    
    // Assert: Saga reached Failed state
    var sagaState = await SagaDbContext.SagaStates.FirstAsync(s => s.OrderId == orderId);
    Assert.That(sagaState.CurrentState, Is.EqualTo("Failed"));
    Assert.That(sagaState.FailureReason, Contains.Substring("Payment"));
    
    // Assert: Order marked failed
    var order = await OrderDbContext.Orders.FirstAsync(o => o.OrderId == orderId);
    Assert.That(order.Status, Is.EqualTo("Failed"));
    
    // Assert: Inventory was released (CRITICAL)
    var ledgerEntries = await InventoryDbContext.ReservationLedger
        .Where(l => l.OrderId == orderId)
        .ToListAsync();
    
    var reservedQty = ledgerEntries.Where(l => l.Type == "Reserve").Sum(l => l.Quantity);
    var releasedQty = ledgerEntries.Where(l => l.Type == "Release").Sum(l => l.Quantity);
    
    Assert.That(reservedQty, Is.EqualTo(5), "Should have one reservation");
    Assert.That(releasedQty, Is.EqualTo(5), "Should have one release (compensation)");
    
    // Assert: Stock returned to original
    var product = await InventoryDbContext.Products.FirstAsync(p => p.ProductId == productId);
    Assert.That(product.Stock, Is.EqualTo(100), "Stock should be restored to original");
    
    // Assert: Failure notification sent
    var notification = await NotificationDbContext.Notifications
        .FirstAsync(n => n.OrderId == orderId && n.EventType == "OrderFailed");
    Assert.That(notification.Status, Is.EqualTo("Delivered"));
}
```

#### Scenario 3: 🔴 CRITICAL - Orchestrator Crash Recovery

```csharp
[Test]
public async Task OrchestratorCrash_Mid_Saga_Recovers_After_Restart()
{
    // Arrange: Place order and let saga progress
    var customerId = Guid.NewGuid();
    var productId = Guid.NewGuid();
    
    InventoryDbContext.Products.Add(new Product { ProductId = productId, Stock = 100 });
    await InventoryDbContext.SaveChangesAsync();
    
    var orderResponse = await _orderService.PlaceOrder(new PlaceOrderRequest
    {
        CustomerId = customerId,
        Items = new[] { new OrderItem { ProductId = productId, Quantity = 5 } }
    });
    
    var orderId = orderResponse.OrderId;
    
    // Act: Start saga
    await _sagaOrchestrator.HandleOrderPlacedEvent(new OrderPlacedEvent { OrderId = orderId });
    
    // Let it reach "ReservingInventory" state
    await Task.Delay(500);
    
    var sagaBeforeCrash = await SagaDbContext.SagaStates.FirstAsync(s => s.OrderId == orderId);
    Assert.That(sagaBeforeCrash.CurrentState, Is.EqualTo("ReservingInventory"));
    
    // Simulate orchestrator crash (in real scenario, kill process)
    // For testing, we just dispose the orchestrator and create a new one
    _sagaOrchestrator.Dispose();
    
    // Wait to ensure crash is processed
    await Task.Delay(1000);
    
    // Act: Restart orchestrator (load sagas from DB)
    _sagaOrchestrator = new SagaOrchestrator(SagaDbContext, ProducerFactory);
    await _sagaOrchestrator.StartAsync();
    
    // Wait for saga to resume and complete
    await Task.Delay(2000);
    
    // Assert: Saga completed from persisted state
    var sagaAfterRecovery = await SagaDbContext.SagaStates.FirstAsync(s => s.OrderId == orderId);
    Assert.That(sagaAfterRecovery.CurrentState, Is.AnyOf("Completed", "Failed"), 
        "Saga should reach terminal state after recovery");
    
    // Assert: Order reached terminal state
    var order = await OrderDbContext.Orders.FirstAsync(o => o.OrderId == orderId);
    Assert.That(order.Status, Is.AnyOf("Confirmed", "Failed"));
    
    // Assert: Stock correct (either reserved or released, not both)
    var product = await InventoryDbContext.Products.FirstAsync(p => p.ProductId == productId);
    Assert.That(product.Stock, Is.AnyOf(95, 100), 
        "Stock should be either decremented (completed) or original (failed)");
}
```

#### Scenario 4: Duplicate Message Suppression

```csharp
[Test]
public async Task DuplicateMessage_Suppressed_By_Inbox_Pattern()
{
    // Arrange
    var orderId = Guid.NewGuid();
    var productId = Guid.NewGuid();
    var messageId = Guid.NewGuid(); // SAME messageId for both messages
    
    InventoryDbContext.Products.Add(new Product { ProductId = productId, Stock = 100 });
    await InventoryDbContext.SaveChangesAsync();
    
    var cmd = new ReserveInventoryCommand
    {
        OrderId = orderId,
        ProductId = productId,
        Quantity = 5,
        MessageId = messageId
    };
    
    // Act: Publish same message twice
    await _producer.ProduceAsync("inventory.commands", cmd);
    await _producer.ProduceAsync("inventory.commands", cmd); // Duplicate
    
    // Let consumer process both
    await Task.Delay(1000);
    
    // Assert: Stock decremented only once
    var product = await InventoryDbContext.Products.FirstAsync(p => p.ProductId == productId);
    Assert.That(product.Stock, Is.EqualTo(95), "Stock should be decremented only once");
    
    // Assert: Only one ledger entry
    var ledgerCount = await InventoryDbContext.ReservationLedger
        .CountAsync(l => l.MessageId == messageId);
    Assert.That(ledgerCount, Is.EqualTo(1), "Only one ledger entry for this messageId");
    
    // Assert: Inbox has one entry
    var inboxCount = await InventoryDbContext.InboxMessages
        .CountAsync(m => m.MessageId == messageId);
    Assert.That(inboxCount, Is.EqualTo(1), "Only one inbox entry");
}
```

#### Scenario 5: Poison Message → DLQ

```csharp
[Test]
public async Task FailedMessage_RoutedTo_DLQ()
{
    // Arrange: Create a malformed message that will fail processing
    var malformedCommand = new ReserveInventoryCommand
    {
        OrderId = Guid.NewGuid(),
        ProductId = Guid.Empty, // Invalid: empty GUID
        Quantity = 5,
        MessageId = Guid.NewGuid()
    };
    
    // Act: Produce malformed message
    await _producer.ProduceAsync("inventory.commands", malformedCommand);
    
    // Let consumer attempt processing (will fail N times, then route to DLQ)
    await Task.Delay(5000); // Wait for retry exhaustion
    
    // Assert: Message in DLQ
    var dlqMessages = await _consumer.ConsumeAsync("inventory.commands.dlq");
    Assert.That(dlqMessages, Has.Count.GreaterThan(0));
    
    var dlqMessage = dlqMessages.FirstOrDefault(m => m.MessageId == malformedCommand.MessageId);
    Assert.That(dlqMessage, Is.Not.Null);
    
    // Assert: Error context preserved
    Assert.That(dlqMessage.Headers["RetryCount"], Is.EqualTo("3")); // Max retries
    Assert.That(dlqMessage.Headers["ErrorMessage"], Contains.Substring("validation"));
}
```

#### Scenario 6: Concurrent Orders (No Partition Blocking)

```csharp
[Test]
public async Task ConcurrentOrders_NoPartitionBlocking()
{
    // Arrange: Create 100 products with stock
    var productIds = Enumerable.Range(0, 100)
        .Select(i => Guid.NewGuid())
        .ToList();
    
    foreach (var productId in productIds)
    {
        InventoryDbContext.Products.Add(new Product { ProductId = productId, Stock = 1000 });
    }
    await InventoryDbContext.SaveChangesAsync();
    
    // Act: Place 100 orders concurrently
    var tasks = Enumerable.Range(0, 100).Select(i =>
        _orderService.PlaceOrder(new PlaceOrderRequest
        {
            CustomerId = Guid.NewGuid(),
            Items = new[] { new OrderItem { ProductId = productIds[i], Quantity = 5 } }
        })
    );
    
    var sw = Stopwatch.StartNew();
    var responses = await Task.WhenAll(tasks);
    sw.Stop();
    
    // Wait for all sagas to complete
    await Task.Delay(5000);
    
    // Assert: All orders completed within reasonable time
    Assert.That(sw.ElapsedMilliseconds, Is.LessThan(10000), 
        "Concurrent orders should complete within 10s (no partition blocking)");
    
    // Assert: All orders reached terminal state
    var orders = await OrderDbContext.Orders.ToListAsync();
    Assert.That(orders, Has.Count.EqualTo(100));
    Assert.That(orders.All(o => o.Status is "Confirmed" or "Failed"), 
        "All orders should reach terminal state");
    
    // Assert: No stuck orders
    var pendingCount = orders.Count(o => o.Status == "Pending");
    Assert.That(pendingCount, Is.EqualTo(0), "No orders should be stuck in Pending");
}
```

---

## Part 3: Test Execution & Reporting

### Running Tests by Category

```bash
# Run all property-based tests (unit level)
dotnet test --filter "Category=Property"

# Run all integration tests
dotnet test --filter "Category=Integration"

# Run CRITICAL compensation test
dotnet test --filter "Category=Critical"

# Run specific module tests
dotnet test Orders.Service.Tests

# Run with live output
dotnet test --logger "console;verbosity=detailed"

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover

# Run only failing tests
dotnet test --filter "TestResult=Failed"
```

### Test Reporting

**Property-Based Test Report**:
```
✅ Property 1.1.1: Idempotent Order Creation
   - Generated 100 test cases
   - All passed
   - Edge cases tested: quantity=0, quantity=max, duplicate messageIds

✅ Property 2.1.1: Idempotent Reservation
   - Generated 100 test cases
   - All passed
   - Stock range: 1-10000
   - Quantity range: 1-10000

✅ Property 5.2.1: Compensation Completeness
   - Generated 50 test cases
   - All passed
   - Verified: All completed steps compensated on failure
```

**Integration Test Report**:
```
✅ HappyPath_Test
   - Order placed, reserved, charged, completed
   - Duration: 2.3s
   - Trace: 1 Correlation ID spanning 5 services in Jaeger

✅ PaymentFailure_Compensation_Test 🔴 CRITICAL
   - Order placed, reserved
   - Payment fails (rate=1.0)
   - InventoryReleased event published
   - Stock restored to original level
   - Order marked Failed
   - Duration: 2.8s

✅ OrchestratorCrash_Recovery_Test 🔴 CRITICAL
   - Saga in mid-flight (ReservingInventory state)
   - Orchestrator killed
   - Orchestrator restarted
   - Saga loaded from DB and resumed
   - Final state reached (Completed or Failed)
   - Duration: 4.5s
```

### Continuous Integration (GitHub Actions)

```yaml
name: Test Suite

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    
    services:
      postgres:
        image: postgres:15
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        
      kafka:
        image: confluentinc/cp-kafka:latest
        options: >-
          --health-cmd kafka-broker-api-versions
          --health-interval 10s
    
    steps:
    - uses: actions/checkout@v3
    
    - uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
    
    - run: dotnet build
    
    - run: dotnet test --logger "trx;LogFileName=test-results.trx"
    
    - uses: dorny/test-reporter@v1
      if: always()
      with:
        name: Test Results
        path: '**/test-results.trx'
        reporter: 'dotnet trx'
    
    - run: dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover
    
    - uses: codecov/codecov-action@v3
      with:
        files: ./coverage.opencover.xml
```

---

## Part 4: Debugging Failed Tests

### Property-Based Test Failures

**Problem**: FsCheck found a failing input, but which one?

**Solution**: FsCheck shrinks the input to the minimal failing case.

```
FsCheck.Xunit.PropertyFailedException: 
  Falsifiable, after 42 tests, 6 shrinks (seed: 1234567890):
  
  ReserveInventoryCommand
    ProductId = 12345678-1234-1234-1234-123456789012
    Quantity = 0
    ...
  
  Error: Quantity must be > 0
```

**Action**: 
1. Identify the minimal failing input
2. Add `Assume.That()` constraints to avoid invalid inputs
3. Or fix the implementation to handle the edge case

### Integration Test Failures

**Problem**: Real Kafka/Postgres is slower than tests expect

**Solution**: Increase timeouts and add retry loops

```csharp
// Bad
await Task.Delay(1000);
var order = await db.Orders.FirstAsync(o => o.OrderId == orderId);

// Good
var order = await Retry(
    async () => await db.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId),
    maxAttempts: 10,
    delay: TimeSpan.FromMilliseconds(500)
);

Assert.That(order, Is.Not.Null);
```

### Trace & Log Analysis

**Problem**: Test fails but reason unclear

**Solution**: Check logs and Jaeger

```bash
# View logs from containers
docker logs orders-service
docker logs inventory-service

# View Jaeger trace
open http://localhost:16686
# Search for Correlation-ID from test output
```

---

## Part 5: Test-Driven Development (TDD) Workflow

### For Each Module

1. **Write the Property-Based Test First** (Property 1.1.1, etc.)
   ```csharp
   [Property]
   public void ReserveInventory_DuplicateMessageId_Isidempotent(...)
   {
       // Should fail: implementation doesn't exist yet
   }
   ```

2. **Run Tests** → They fail ✅
   ```bash
   dotnet test Orders.Service.Tests --filter "Property1.1.1"
   ```

3. **Implement Code** (just enough to pass the test)
   ```csharp
   public void ReserveInventory(cmd, messageId)
   {
       if (IsProcessed(messageId)) return;
       // ... reserve logic
       MarkAsProcessed(messageId);
   }
   ```

4. **Run Tests** → They pass ✅
   ```bash
   dotnet test Orders.Service.Tests
   ```

5. **Refactor** (improve code quality)

6. **Move to Next Test** → Repeat

### Example: Building Idempotent Reservation

**Day 1: Write Property Test**
```csharp
[Property]
public void ReserveInventory_DuplicateMessageId_Idempotent(...)
{
    // Test written, fails (code doesn't exist)
}
```

**Day 2: Implement Inbox Pattern**
```csharp
public class ReserveInventoryHandler
{
    public void Handle(ReserveInventoryCommand cmd)
    {
        // Check inbox
        if (db.InboxMessages.Any(m => m.MessageId == cmd.MessageId))
            return; // Already processed
        
        // Process
        var ledger = new ReservationLedger 
        { 
            OrderId = cmd.OrderId, 
            Quantity = cmd.Quantity,
            MessageId = cmd.MessageId 
        };
        db.ReservationLedger.Add(ledger);
        
        // Atomically insert inbox record
        db.InboxMessages.Add(new InboxMessage 
        { 
            MessageId = cmd.MessageId, 
            ProcessedAt = DateTime.UtcNow 
        });
        
        db.SaveChanges();
    }
}
```

**Day 3: Run Property Test** → Passes ✅

**Day 4: Write Integration Test**
```csharp
[Test]
public async Task ReserveInventory_WithRealPostgres_IsIdempotent()
{
    // Use Testcontainers Postgres
    // Verify idempotency with real DB
}
```

**Day 5: Run Integration Test** → Passes ✅

---

## Test Checklist

- [ ] Phase 0: Trivial Kafka ping-pong works end-to-end
- [ ] Phase 1: Order Service — 9 properties passing + integration tests
- [ ] Phase 2: Inventory Service — 13 properties passing + integration tests
- [ ] Phase 3: Payment Service — 7 properties passing + configurable failure works
- [ ] Phase 4: Kafka Layer — 12 properties passing + DLQ routing verified
- [ ] Phase 5: Notification Service — 8 properties passing + retry backoff verified
- [ ] Phase 6: Saga Orchestrator — 10 properties passing, **especially compensation (5.2.x)**
- [ ] Phase 6: **Compensation Test** — Force payment failure, verify inventory released
- [ ] Phase 6: **Orchestrator Recovery Test** — Kill mid-saga, restart, verify resume
- [ ] Phase 9: **Happy Path Integration Test** — Order → Complete
- [ ] Phase 9: **Payment Failure Integration Test** — Order → Fail with compensation
- [ ] Phase 9: **Orchestrator Recovery Integration Test** — Crash → Recovery
- [ ] Phase 9: **Concurrent Orders Test** — 100 orders, no partition blocking
- [ ] Phase 9: **Trace Propagation** — One Correlation ID visible in Jaeger spanning all services
- [ ] Daily: `dotnet build` succeeds (no warnings)
- [ ] Daily: `dotnet test` passes (all tests)

---

**Success Criterion**: On Day 28, run:
```bash
dotnet test --filter "Category=Critical"
```

Should see:
```
✅ PaymentFailure_Compensation_Test PASSED
✅ OrchestratorCrash_Recovery_Test PASSED
✅ HappyPath_Test PASSED
```

Then verify in Jaeger:
- One trace ID spanning Order → Saga → Inventory → Payment → Notification services
- All spans and logs include Correlation ID
- Latency < 3s end-to-end

