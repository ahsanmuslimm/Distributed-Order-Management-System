# Distributed Order Management System - Technical Design & Module Decomposition

## Executive Summary

This document translates the 25+ requirements from the Requirements Document into a phased module architecture with explicit correctness properties, property-based test suites, and prototype implementation milestones. Each module ships with a working prototype only after all property-based tests pass.

**Design Philosophy:**
- Property-driven design: Every correctness property has an executable test
- Fail-fast composition: Modules are built and validated independently before integration
- Test-gate prototypes: No prototype artifact shipped until 100% property coverage passes
- Explicit failure handling: Failure modes are tested, not assumed

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         CLIENT / UI                              │
└──────────────────────────────┬──────────────────────────────────┘
                               │ HTTP
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                    API GATEWAY (YARP)                            │
│  ├─ Routing (Service Discovery)                                 │
│  ├─ Rate Limiting (Fixed-Window per Consumer)                   │
│  └─ Circuit Breaking (Polly)                                    │
└──────┬───────────────────┬────────────────────────────────┬─────┘
       │ HTTP              │ HTTP                          │ HTTP
       ▼                   ▼                               ▼
┌─────────────────┐ ┌──────────────────┐ ┌──────────────────────┐
│ ORDER SERVICE   │ │ INVENTORY        │ │ SAGA ORCHESTRATOR    │
│                 │ │ SERVICE          │ │                      │
│ ├─ Place Order  │ │ ├─ Get Catalog   │ │ ├─ Initialize Saga  │
│ ├─ Query Status │ │ └─ Query Stock   │ │ ├─ State Transitions │
│ └─ Consume Cmds │ │ └─ Cache Layer   │ │ ├─ Compensation     │
│                 │ │ (Redis)          │ │ └─ Timeout Handling │
│ DB: Postgres    │ │ DB: Postgres     │ │ DB: Postgres        │
└────────┬────────┘ └───────┬──────────┘ └──────────┬───────────┘
         │ Kafka             │ Kafka               │ Kafka
         │ (events +         │ (commands +         │ (all events)
         │  commands)        │  events)            │
         └─────────┬─────────┴─────────────────────┘
                   │
                   ▼
        ┌──────────────────────┐
        │   KAFKA CLUSTER      │
        │ ├─ order.events      │
        │ ├─ inventory.cmds    │
        │ ├─ inventory.events  │
        │ ├─ payment.cmds      │
        │ ├─ payment.events    │
        │ ├─ order.cmds        │
        │ ├─ notification.events
        │ └─ *.dlq (5 topics)  │
        └──────────┬───────────┘
                   │ Kafka
         ┌─────────┼─────────┐
         ▼         ▼         ▼
    ┌─────────┐ ┌─────────┐ ┌──────────────┐
    │PAYMENT  │ │NOTIF.   │ │ OBSERVABILITY
    │SERVICE  │ │SERVICE  │ │ ├─ Jaeger    │
    │         │ │         │ │ ├─ OTel      │
    │├─Charge │ │├─Consume│ │ └─ Metrics   │
    │├─Refund │ │└─Notify │ │              │
    │         │ │         │ │ REDIS        │
    │DB:      │ │DB:      │ │ ├─ Cache     │
    │Postgres │ │Postgres │ │ │            │
    └─────────┘ └─────────┘ └──────────────┘
```

---

## Module 1: Order Service

### Design Constraints

- **Single Responsibility**: Owns order records and order-status reads; does NOT call other services synchronously
- **Async First**: All state changes published to Kafka, consumed asynchronously
- **Inbox Pattern**: Every Kafka message consumed via inbox (deduplicate, then apply)
- **Idempotency**: Every message has unique MessageId; processed messages stored durably

### Component Architecture

```
Order Service (ASP.NET Core)
│
├─ HTTP API Layer
│  ├─ POST /api/orders (Place Order)
│  ├─ GET /api/orders/{id} (Query Status)
│  └─ GET /api/orders/{id}/history (Get State Transitions)
│
├─ Application Layer
│  ├─ PlaceOrderHandler (CreateOrder, PublishEvent)
│  ├─ QueryOrderStatusHandler
│  └─ CommandConsumer (Consume ConfirmOrder, FailOrder)
│
├─ Domain Layer
│  ├─ Order Entity (Aggregate Root)
│  │  ├─ OrderId (PK)
│  │  ├─ CustomerId
│  │  ├─ Items ([]OrderItem)
│  │  ├─ Status (Enum: Pending|Reserved|Charged|Confirmed|Failed)
│  │  ├─ CreatedAt, LastUpdatedAt
│  │  └─ SagaId (FK to Saga State)
│  │
│  └─ OrderStatusTransition (Event-driven updates)
│     ├─ OrderId (FK)
│     ├─ FromStatus, ToStatus
│     ├─ Reason (why status changed)
│     └─ Timestamp
│
├─ Persistence Layer (EF Core)
│  ├─ OrderDbContext
│  │  ├─ Orders (DbSet)
│  │  ├─ OrderItems (DbSet)
│  │  ├─ OrderStatusTransitions (DbSet)
│  │  └─ InboxMessages (DbSet) — idempotency store
│  │
│  └─ Migrations (Code-First)
│
├─ Messaging Layer (MassTransit + Kafka)
│  ├─ Producer: PublishOrderPlacedEvent
│  ├─ Consumer: ConsumeConfirmOrderCommand
│  ├─ Consumer: ConsumeFailOrderCommand
│  └─ Consumer Error Handler (DLQ routing)
│
└─ Observability
   ├─ Correlation ID Extraction/Propagation
   ├─ Structured Logging (Serilog)
   └─ OTel Spans (HTTP, DB, Kafka)
```

### Data Model

```sql
-- Orders table
CREATE TABLE Orders (
    OrderId UUID PRIMARY KEY,
    CustomerId UUID NOT NULL,
    Status VARCHAR(50) NOT NULL,
    SagaId UUID REFERENCES SagaState(SagaId),
    CreatedAt TIMESTAMPTZ NOT NULL,
    LastUpdatedAt TIMESTAMPTZ NOT NULL,
    Version INT NOT NULL (optimistic concurrency)
);

-- OrderItems table
CREATE TABLE OrderItems (
    OrderItemId UUID PRIMARY KEY,
    OrderId UUID NOT NULL REFERENCES Orders(OrderId),
    ProductId UUID NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(10,2) NOT NULL
);

-- OrderStatusTransitions table (audit)
CREATE TABLE OrderStatusTransitions (
    TransitionId UUID PRIMARY KEY,
    OrderId UUID NOT NULL REFERENCES Orders(OrderId),
    FromStatus VARCHAR(50),
    ToStatus VARCHAR(50) NOT NULL,
    Reason VARCHAR(255),
    Timestamp TIMESTAMPTZ NOT NULL,
    CorrelationId UUID NOT NULL
);

-- InboxMessages table (idempotency store)
CREATE TABLE InboxMessages (
    MessageId UUID PRIMARY KEY,
    OrderId UUID NOT NULL,
    MessageType VARCHAR(100) NOT NULL,
    Payload TEXT NOT NULL,
    ProcessedAt TIMESTAMPTZ NOT NULL,
    CorrelationId UUID NOT NULL
);
```

### Property-Based Test Suite

**Test File**: `OrderService.Tests/OrderServicePropertyTests.cs`

```csharp
[TestFixture]
public class OrderServiceIdempotencyTests
{
    // Property 1.1.1: Idempotent Order Creation
    [Property]
    public void PlaceOrder_WithDuplicateMessageId_ProducesIdempotentResult(
        Guid orderId, 
        Guid customerId, 
        List<OrderItem> items,
        Guid messageId)
    {
        // Arrange
        var command = new PlaceOrderCommand 
        { 
            OrderId = orderId,
            CustomerId = customerId,
            Items = items,
            MessageId = messageId
        };

        // Act
        var result1 = _handler.Handle(command);
        var result2 = _handler.Handle(command); // Replay with same messageId

        // Assert
        Assert.That(result1.OrderId, Is.EqualTo(result2.OrderId));
        Assert.That(result1.Status, Is.EqualTo(result2.Status));
        
        // Verify DB has exactly one order
        var orderCount = _dbContext.Orders.Count(o => o.OrderId == orderId);
        Assert.That(orderCount, Is.EqualTo(1));
    }

    // Property 1.1.3: Event-State Correspondence
    [Property]
    public void PlaceOrder_PublishedEvent_MatchesPersistentState(
        PlaceOrderCommand command)
    {
        // Act
        var result = _handler.Handle(command);
        var publishedEvent = _messageCapture.GetPublishedEvent<OrderPlacedEvent>();
        var persistedOrder = _dbContext.Orders.First(o => o.OrderId == command.OrderId);

        // Assert
        Assert.That(publishedEvent.OrderId, Is.EqualTo(persistedOrder.OrderId));
        Assert.That(publishedEvent.CustomerId, Is.EqualTo(persistedOrder.CustomerId));
        Assert.That(publishedEvent.Status, Is.EqualTo("Pending"));
    }

    // Property 1.1.4: Message_ID Uniqueness Enforcement
    [Property]
    public void InboxMessages_ContainsExactlyOneEntry_PerMessageId(
        Guid messageId,
        int duplicateCount)
    {
        Assume.That(duplicateCount, Is.GreaterThan(1));

        // Act: Process same message N times
        for (int i = 0; i < duplicateCount; i++)
        {
            _handler.Handle(CreateCommandWithMessageId(messageId));
        }

        // Assert
        var inboxCount = _dbContext.InboxMessages
            .Count(m => m.MessageId == messageId);
        Assert.That(inboxCount, Is.EqualTo(1));
    }
}

[TestFixture]
public class OrderStatusQueryPropertyTests
{
    // Property 1.2.1: Query Result Consistency
    [Property]
    public void QueryOrder_ConsecutiveQueries_ProduceIdenticalState(
        Guid orderId)
    {
        // Arrange
        _testDataGenerator.CreateOrder(orderId);

        // Act
        var query1 = _queryHandler.Handle(new GetOrderStatusQuery { OrderId = orderId });
        var query2 = _queryHandler.Handle(new GetOrderStatusQuery { OrderId = orderId });

        // Assert (within same transaction)
        Assert.That(query1.Status, Is.EqualTo(query2.Status));
        Assert.That(query1.Items, Is.EquivalentTo(query2.Items));
        Assert.That(query1.LastUpdatedAt, Is.EqualTo(query2.LastUpdatedAt));
    }

    // Property 1.2.2: Status Progression Validity
    [Property]
    public void QueryOrder_Status_IsValidStateValue(Guid orderId)
    {
        var validStates = new[] 
        { 
            "Pending", "Reserved", "Charged", "Completed", "Failed", "Compensated" 
        };

        var query = _queryHandler.Handle(new GetOrderStatusQuery { OrderId = orderId });

        Assert.That(query.Status, Is.AnyOf(validStates));
    }

    // Property 1.2.3: Timestamp Monotonicity
    [Property]
    public void Order_Timestamps_SatisfyMonotonicityConstraint(Guid orderId)
    {
        var order = _dbContext.Orders.First(o => o.OrderId == orderId);

        Assert.That(order.CreatedAt, Is.LessThanOrEqualTo(order.LastUpdatedAt));
    }
}

[TestFixture]
public class CorrelationIdPropagationPropertyTests
{
    // Property 1.3.1: Correlation_ID Presence
    [Property]
    public void ApiResponse_IncludesCorrelationId_AlwaysPresent(
        HttpRequestMessage request,
        string? providedCorrelationId)
    {
        // Arrange
        if (providedCorrelationId != null)
        {
            request.Headers.Add("Correlation-ID", providedCorrelationId);
        }

        // Act
        var response = _httpClient.SendAsync(request).Result;
        var responseCorrelationId = response.Headers.GetValues("Correlation-ID").FirstOrDefault();

        // Assert
        Assert.That(responseCorrelationId, Is.Not.Null);
        if (providedCorrelationId != null)
        {
            Assert.That(responseCorrelationId, Is.EqualTo(providedCorrelationId));
        }
        else
        {
            Assert.That(Guid.TryParse(responseCorrelationId, out _), Is.True);
        }
    }

    // Property 1.3.2: Correlation_ID Immutability in Event
    [Property]
    public void PublishedEvent_CorrelationId_MatchesResponseHeader(
        string correlationId,
        PlaceOrderCommand command)
    {
        // Act
        var response = _handler.Handle(command, correlationId);
        var publishedEvent = _messageCapture.GetPublishedEvent<OrderPlacedEvent>();

        // Assert
        Assert.That(publishedEvent.CorrelationId, Is.EqualTo(correlationId));
    }
}
```

### Test Execution Strategy

1. **Unit Tests** (FsCheck/QuickCheck): Properties with in-memory database
2. **Integration Tests** (Testcontainers): Properties with real PostgreSQL
3. **Messaging Tests**: Verify event publishing with MassTransit test harness
4. **Property Coverage Gate**: 100% property acceptance criteria coverage before prototype release

### Prototype Milestone: Order Service v0.1

**Acceptance Gate**: All property-based tests pass + integration tests with Testcontainers

**Deliverables**:
- [ ] HTTP API (Place Order, Query Status, History)
- [ ] EF Core DbContext + Migrations
- [ ] Inbox Pattern implementation
- [ ] MassTransit Producer (OrderPlaced event)
- [ ] Correlation ID propagation in responses
- [ ] Structured logging with Serilog
- [ ] All property-based tests passing

**Out of Scope for v0.1**:
- Command consumption (ConfirmOrder, FailOrder) — handled in Saga Orchestrator integration
- Redis caching — handled in Inventory Service
- Circuit breaking — handled in Gateway
- Advanced observability (spans) — added in Phase 5

---

## Module 2: Inventory Service

### Design Constraints

- **Ledger of Truth**: All stock movements recorded in immutable ledger
- **Read Cache Decoupling**: Redis used only for catalog reads, never for reservation decisions
- **Reservation Ledger**: Every reservation creates immutable entry with MessageId (prevents double-reserve)
- **Stock Lock**: Row-level pessimistic lock for reservation decision, not cache-based

### Component Architecture

```
Inventory Service (ASP.NET Core)
│
├─ HTTP API Layer
│  ├─ GET /api/catalog (Browse Products)
│  ├─ GET /api/products/{id}/stock (Query Stock)
│  └─ GET /api/health
│
├─ Application Layer
│  ├─ GetCatalogHandler (with Redis cache-aside)
│  ├─ GetStockHandler (query only, no cache for decisions)
│  └─ CommandConsumer (Consume ReserveInventory, ReleaseInventory)
│
├─ Domain Layer
│  ├─ Product Aggregate
│  │  ├─ ProductId (PK)
│  │  ├─ Name, Description, Price
│  │  ├─ CurrentStock (denormalized from ledger)
│  │  └─ ReservedStock (denormalized from ledger)
│  │
│  ├─ ReservationLedger Entity
│  │  ├─ LedgerId (PK)
│  │  ├─ OrderId, ProductId
│  │  ├─ Quantity
│  │  ├─ Type (Reserve|Release)
│  │  ├─ MessageId (unique per operation)
│  │  ├─ Status (Completed|Compensated)
│  │  └─ CreatedAt
│  │
│  └─ StockCalculator (Aggregate Ledger → Current Stock)
│
├─ Persistence Layer (EF Core)
│  ├─ InventoryDbContext
│  │  ├─ Products (DbSet)
│  │  ├─ ReservationLedger (DbSet)
│  │  ├─ InboxMessages (DbSet) — idempotency store
│  │  └─ CacheInvalidationLog (DbSet) — for Redis sync
│  │
│  └─ Migrations (Code-First)
│
├─ Caching Layer (Redis)
│  ├─ CacheKeyPrefix: "catalog:{productId}"
│  ├─ CacheTTL: 60 seconds
│  ├─ Cache-Aside Strategy
│  │  ├─ On Read: Check Redis → Fallback to DB
│  │  ├─ On Write: Update DB → Invalidate Redis
│  │  └─ On Miss: Rebuild from Ledger
│  │
│  └─ CacheConsistencyMonitor (Async background job)
│
├─ Messaging Layer (MassTransit + Kafka)
│  ├─ Producer: PublishInventoryReservedEvent
│  ├─ Producer: PublishInventoryReleasedEvent
│  ├─ Consumer: ConsumeReserveInventoryCommand
│  ├─ Consumer: ConsumeReleaseInventoryCommand
│  └─ Consumer Error Handler (DLQ routing)
│
└─ Observability
   ├─ Correlation ID Extraction/Propagation
   ├─ Structured Logging (Serilog)
   └─ OTel Spans (HTTP, DB, Kafka, Redis)
```

### Data Model

```sql
-- Products table
CREATE TABLE Products (
    ProductId UUID PRIMARY KEY,
    Name VARCHAR(255) NOT NULL,
    Description TEXT,
    Price DECIMAL(10,2) NOT NULL,
    InitialStock INT NOT NULL,
    CreatedAt TIMESTAMPTZ NOT NULL
);

-- ReservationLedger table (immutable audit trail)
CREATE TABLE ReservationLedger (
    LedgerId UUID PRIMARY KEY,
    OrderId UUID NOT NULL,
    ProductId UUID NOT NULL REFERENCES Products(ProductId),
    Quantity INT NOT NULL CHECK (Quantity > 0),
    Type VARCHAR(20) NOT NULL CHECK (Type IN ('Reserve', 'Release')),
    MessageId UUID NOT NULL UNIQUE,
    Status VARCHAR(20) NOT NULL CHECK (Status IN ('Completed', 'Compensated')),
    CreatedAt TIMESTAMPTZ NOT NULL,
    CorrelationId UUID NOT NULL,
    CONSTRAINT ledger_order_product UNIQUE (OrderId, ProductId, Type) -- Prevent duplicate operation
);

-- InboxMessages table (idempotency store)
CREATE TABLE InboxMessages (
    MessageId UUID PRIMARY KEY,
    ProductId UUID NOT NULL REFERENCES Products(ProductId),
    MessageType VARCHAR(100) NOT NULL,
    Payload TEXT NOT NULL,
    ProcessedAt TIMESTAMPTZ NOT NULL,
    CorrelationId UUID NOT NULL
);

-- CacheInvalidationLog table (tracks cache-ledger sync)
CREATE TABLE CacheInvalidationLog (
    InvalidationId UUID PRIMARY KEY,
    ProductId UUID NOT NULL REFERENCES Products(ProductId),
    Reason VARCHAR(255),
    CreatedAt TIMESTAMPTZ NOT NULL
);

-- Create index for ledger queries by product and status
CREATE INDEX idx_ledger_product_status ON ReservationLedger(ProductId, Status);
CREATE INDEX idx_ledger_messageid ON ReservationLedger(MessageId);
```

### Property-Based Test Suite

**Test File**: `InventoryService.Tests/InventoryPropertyTests.cs`

```csharp
[TestFixture]
public class InventoryIdempotencyPropertyTests
{
    // Property 2.1.1: Idempotent Processing
    [Property]
    public void ReserveInventory_WithDuplicateMessageId_DecrementsStockExactlyOnce(
        Guid productId,
        Guid orderId,
        int quantity,
        Guid messageId)
    {
        Assume.That(quantity, Is.GreaterThan(0));
        Assume.That(quantity, Is.LessThanOrEqualTo(1000)); // Reasonable stock limit

        // Arrange
        var initialStock = _testDataGenerator.SetProductStock(productId, 1000);

        var command = new ReserveInventoryCommand
        {
            ProductId = productId,
            OrderId = orderId,
            Quantity = quantity,
            MessageId = messageId
        };

        // Act
        var result1 = _handler.Handle(command);
        var stockAfterFirst = _queryHandler.GetStock(productId);

        var result2 = _handler.Handle(command); // Replay with same messageId
        var stockAfterSecond = _queryHandler.GetStock(productId);

        // Assert
        Assert.That(result1.IsSuccess, Is.True);
        Assert.That(result2.IsSuccess, Is.True);
        Assert.That(stockAfterFirst, Is.EqualTo(initialStock - quantity));
        Assert.That(stockAfterSecond, Is.EqualTo(initialStock - quantity)); // Stock unchanged on replay
    }

    // Property 2.1.2: Ledger Immutability
    [Property]
    public void ReserveInventory_DuplicateMessages_CreateExactlyOneLedgerEntry(
        Guid productId,
        Guid orderId,
        int quantity,
        Guid messageId)
    {
        Assume.That(quantity, Is.GreaterThan(0));

        var command = new ReserveInventoryCommand
        {
            ProductId = productId,
            OrderId = orderId,
            Quantity = quantity,
            MessageId = messageId
        };

        // Act: Process twice
        _handler.Handle(command);
        _handler.Handle(command);

        // Assert
        var ledgerCount = _dbContext.ReservationLedger
            .Count(l => l.MessageId == messageId && l.ProductId == productId);
        Assert.That(ledgerCount, Is.EqualTo(1));
    }

    // Property 2.1.5: Stock Never Negative
    [Property]
    public void ReserveInventory_Stock_NeverBecomesNegative(
        Guid productId,
        List<ReserveInventoryCommand> commands)
    {
        Assume.That(commands.Count, Is.GreaterThan(0));
        
        var initialStock = 100;
        _testDataGenerator.SetProductStock(productId, initialStock);

        // Act: Process all commands (some may fail)
        foreach (var cmd in commands)
        {
            try { _handler.Handle(cmd); } catch { /* Ignore failures */ }
        }

        // Assert
        var currentStock = _queryHandler.GetStock(productId);
        Assert.That(currentStock, Is.GreaterThanOrEqualTo(0));
    }
}

[TestFixture]
public class InventoryReleasePropertyTests
{
    // Property 2.2.1: Release Idempotency
    [Property]
    public void ReleaseInventory_WithDuplicateMessageId_ProducesSameStock(
        Guid productId,
        Guid orderId,
        int reservedQuantity,
        Guid releaseMessageId)
    {
        Assume.That(reservedQuantity, Is.GreaterThan(0));

        // Arrange: First reserve
        _testDataGenerator.SetProductStock(productId, 1000);
        var reserveCmd = new ReserveInventoryCommand 
        { 
            ProductId = productId, 
            OrderId = orderId, 
            Quantity = reservedQuantity,
            MessageId = Guid.NewGuid()
        };
        _handler.Handle(reserveCmd);
        var stockAfterReserve = _queryHandler.GetStock(productId);

        var releaseCmd = new ReleaseInventoryCommand
        {
            ProductId = productId,
            OrderId = orderId,
            Quantity = reservedQuantity,
            MessageId = releaseMessageId
        };

        // Act
        _handler.Handle(releaseCmd);
        var stockAfterFirstRelease = _queryHandler.GetStock(productId);

        _handler.Handle(releaseCmd); // Replay
        var stockAfterSecondRelease = _queryHandler.GetStock(productId);

        // Assert
        Assert.That(stockAfterFirstRelease, Is.EqualTo(stockAfterReserve + reservedQuantity));
        Assert.That(stockAfterSecondRelease, Is.EqualTo(stockAfterFirstRelease)); // Idempotent
    }

    // Property 2.2.2: Reserve-Release Round Trip
    [Property]
    public void ReserveAndRelease_RoundTrip_RestoresInitialStock(
        Guid productId,
        Guid orderId,
        int quantity)
    {
        Assume.That(quantity, Is.GreaterThan(0).And.LessThanOrEqualTo(1000));

        var initialStock = 500;
        _testDataGenerator.SetProductStock(productId, initialStock);

        // Act
        var reserveCmd = new ReserveInventoryCommand 
        { 
            ProductId = productId, 
            OrderId = orderId, 
            Quantity = quantity,
            MessageId = Guid.NewGuid()
        };
        var reservation = _handler.Handle(reserveCmd);

        var releaseCmd = new ReleaseInventoryCommand
        {
            ProductId = productId,
            OrderId = orderId,
            Quantity = quantity,
            MessageId = Guid.NewGuid()
        };
        _handler.Handle(releaseCmd);

        var finalStock = _queryHandler.GetStock(productId);

        // Assert
        Assert.That(finalStock, Is.EqualTo(initialStock));
    }

    // Property 2.2.3: Ledger Debit-Credit Balance
    [Property]
    public void ReservationLedger_TotalCredits_NeverExceedTotalDebits(
        Guid productId)
    {
        var ledger = _dbContext.ReservationLedger
            .Where(l => l.ProductId == productId)
            .ToList();

        var totalDebits = ledger
            .Where(l => l.Type == "Reserve")
            .Sum(l => l.Quantity);

        var totalCredits = ledger
            .Where(l => l.Type == "Release")
            .Sum(l => l.Quantity);

        Assert.That(totalDebits, Is.GreaterThanOrEqualTo(totalCredits));
    }
}

[TestFixture]
public class RedisCachePropertyTests
{
    // Property 2.3.1: Cache-Ledger Consistency
    [Property]
    public void Cache_Stock_MatchesLedgerCalculation(Guid productId)
    {
        var ledgerStock = CalculateStockFromLedger(productId);
        var cacheStock = _cacheService.GetStockFromCache(productId) 
            ?? _cacheService.RebuildCacheFromLedger(productId);

        Assert.That(cacheStock, Is.EqualTo(ledgerStock));
    }

    // Property 2.3.3: TTL Prevents Stale Reads
    [Property]
    public void CacheEntry_Age_IsWithinTTL_OrInvalidated(
        Guid productId,
        TimeSpan? entryAge)
    {
        var entry = _cacheService.GetCacheEntry(productId);
        
        if (entry != null)
        {
            var now = DateTime.UtcNow;
            var age = now - entry.CreatedAt;
            
            Assert.That(
                age < TimeSpan.FromSeconds(60) || entry.IsInvalidated,
                "Cache entry is stale and not marked invalid"
            );
        }
    }

    // Property 2.3.4: Fallback Correctness
    [Property]
    public void StockQuery_WithRedisDown_FallsbackToLedger(
        Guid productId)
    {
        // Simulate Redis down
        _redis.Dispose();

        var stockFromLedger = CalculateStockFromLedger(productId);
        var stockFromService = _queryHandler.GetStock(productId); // Should fallback

        Assert.That(stockFromService, Is.EqualTo(stockFromLedger));
    }
}
```

### Cache Invalidation Strategy

```csharp
// Background job: Periodic cache consistency check
public class CacheConsistencyJob : IHostedService
{
    public async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var products = await _dbContext.Products.ToListAsync(ct);
            
            foreach (var product in products)
            {
                var ledgerStock = await CalculateStockFromLedger(product.ProductId);
                var cacheStock = await _cache.GetAsync(product.ProductId, ct);
                
                if (cacheStock != ledgerStock)
                {
                    _logger.LogWarning($"Cache divergence detected for {product.ProductId}");
                    await _cache.InvalidateAsync(product.ProductId, ct);
                    // Rebuild on next read (cache-aside)
                }
            }
            
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}
```

### Prototype Milestone: Inventory Service v0.1

**Acceptance Gate**: All property-based tests pass + Testcontainers integration (real Postgres + Redis)

**Deliverables**:
- [ ] HTTP API (Browse Catalog, Query Stock)
- [ ] EF Core DbContext + Migrations (Ledger pattern)
- [ ] Inbox Pattern implementation
- [ ] Redis cache-aside implementation
- [ ] Stock calculation from ledger
- [ ] MassTransit Consumer (ReserveInventory, ReleaseInventory commands)
- [ ] MassTransit Producer (InventoryReserved, InventoryReleased events)
- [ ] Correlation ID propagation
- [ ] Structured logging (Serilog)
- [ ] Cache consistency monitoring (background job)
- [ ] All property-based tests passing

**Out of Scope for v0.1**:
- Payment-triggered compensation (handled in Saga integration)
- Rate limiting (handled in Gateway)
- Advanced observability (spans) — Phase 5

---

## Module 3: Payment Service

### Design Constraints

- **Configurable Failure Rate**: Failure rate set via environment variable, injectable for testing
- **Simulated Charge**: No real payment gateway; deterministic failure based on probability
- **Naturally Idempotent Operations**: "Charge for OrderId X" creates or returns existing payment record (create-if-not-exists pattern)
- **Refund Ledger**: Refunds tracked separately from charges

### Component Architecture

```
Payment Service (ASP.NET Core)
│
├─ Application Layer
│  ├─ CommandConsumer (Consume ChargePayment, RefundPayment)
│  └─ PaymentSimulator (Charge with configurable failure rate)
│
├─ Domain Layer
│  ├─ Payment Aggregate
│  │  ├─ PaymentId (PK)
│  │  ├─ OrderId, CustomerId
│  │  ├─ Amount, Currency
│  │  ├─ Status (Pending|Charged|Refunded|Failed)
│  │  ├─ TransactionId (gateway-assigned, simulated)
│  │  ├─ MessageId (unique per charge attempt)
│  │  └─ CreatedAt, ChargedAt, RefundedAt
│  │
│  └─ PaymentFailureInjector
│     ├─ FailureRate (0.0 — 1.0, from config)
│     ├─ DeterministicRandom (for reproducible tests)
│     └─ ShouldFail() → bool
│
├─ Persistence Layer (EF Core)
│  ├─ PaymentDbContext
│  │  ├─ Payments (DbSet)
│  │  ├─ Refunds (DbSet)
│  │  ├─ InboxMessages (DbSet) — idempotency store
│  │  └─ PaymentAttempts (DbSet) — audit trail
│  │
│  └─ Migrations
│
├─ Messaging Layer (MassTransit + Kafka)
│  ├─ Producer: PublishPaymentChargedEvent
│  ├─ Producer: PublishPaymentFailedEvent
│  ├─ Producer: PublishPaymentRefundedEvent
│  ├─ Consumer: ConsumeChargePaymentCommand
│  ├─ Consumer: ConsumeRefundPaymentCommand
│  └─ Consumer Error Handler (DLQ routing)
│
└─ Observability
   ├─ Correlation ID Extraction/Propagation
   ├─ Structured Logging (Serilog)
   └─ OTel Spans
```

### Data Model

```sql
-- Payments table
CREATE TABLE Payments (
    PaymentId UUID PRIMARY KEY,
    OrderId UUID NOT NULL UNIQUE, -- One payment per order
    CustomerId UUID NOT NULL,
    Amount DECIMAL(10,2) NOT NULL CHECK (Amount > 0),
    Currency VARCHAR(3) NOT NULL DEFAULT 'USD',
    Status VARCHAR(20) NOT NULL CHECK (Status IN ('Pending', 'Charged', 'Refunded', 'Failed')),
    TransactionId VARCHAR(100),
    MessageId UUID NOT NULL UNIQUE, -- Unique per charge attempt
    CreatedAt TIMESTAMPTZ NOT NULL,
    ChargedAt TIMESTAMPTZ,
    RefundedAt TIMESTAMPTZ,
    CorrelationId UUID NOT NULL,
    Version INT NOT NULL (optimistic concurrency)
);

-- Refunds table
CREATE TABLE Refunds (
    RefundId UUID PRIMARY KEY,
    PaymentId UUID NOT NULL REFERENCES Payments(PaymentId),
    Amount DECIMAL(10,2) NOT NULL,
    Reason VARCHAR(255),
    MessageId UUID NOT NULL UNIQUE, -- Unique per refund attempt
    Status VARCHAR(20) NOT NULL CHECK (Status IN ('Pending', 'Completed', 'Failed')),
    CreatedAt TIMESTAMPTZ NOT NULL,
    ProcessedAt TIMESTAMPTZ,
    CorrelationId UUID NOT NULL
);

-- PaymentAttempts table (audit trail of all charge attempts)
CREATE TABLE PaymentAttempts (
    AttemptId UUID PRIMARY KEY,
    PaymentId UUID NOT NULL REFERENCES Payments(PaymentId),
    Attempt INT NOT NULL,
    Result VARCHAR(20) NOT NULL CHECK (Result IN ('Success', 'Failed', 'Timeout')),
    ErrorMessage TEXT,
    AttemptedAt TIMESTAMPTZ NOT NULL,
    CorrelationId UUID NOT NULL
);

-- InboxMessages table (idempotency store)
CREATE TABLE InboxMessages (
    MessageId UUID PRIMARY KEY,
    OrderId UUID NOT NULL,
    MessageType VARCHAR(100) NOT NULL,
    Payload TEXT NOT NULL,
    ProcessedAt TIMESTAMPTZ NOT NULL,
    CorrelationId UUID NOT NULL
);
```

### Property-Based Test Suite

**Test File**: `PaymentService.Tests/PaymentPropertyTests.cs`

```csharp
[TestFixture]
public class PaymentIdempotencyPropertyTests
{
    // Property 3.1.1: Idempotent Payment Processing
    [Property]
    public void ChargePayment_WithDuplicateMessageId_ProducesIdenticalPaymentRecord(
        Guid orderId,
        Guid customerId,
        decimal amount,
        Guid messageId)
    {
        Assume.That(amount, Is.GreaterThan(0).And.LessThan(10000));

        var command = new ChargePaymentCommand
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            MessageId = messageId,
            FailureRate = 0.0 // Force success for this test
        };

        // Act
        var result1 = _handler.Handle(command);
        var payment1 = _dbContext.Payments.First(p => p.OrderId == orderId);

        var result2 = _handler.Handle(command); // Replay with same messageId
        var payment2 = _dbContext.Payments.First(p => p.OrderId == orderId);

        // Assert
        Assert.That(result1.IsSuccess, Is.True);
        Assert.That(result2.IsSuccess, Is.True);
        Assert.That(result1.PaymentId, Is.EqualTo(result2.PaymentId));
        Assert.That(result1.TransactionId, Is.EqualTo(result2.TransactionId));
        Assert.That(payment1.TransactionId, Is.EqualTo(payment2.TransactionId));
        
        // Verify only one payment record
        var paymentCount = _dbContext.Payments.Count(p => p.OrderId == orderId);
        Assert.That(paymentCount, Is.EqualTo(1));
    }

    // Property 3.1.2: Failure Rate Distribution
    [Property]
    public void ChargePayment_FailureRate_MatchesConfiguredProbability(
        decimal configuredFailureRate)
    {
        Assume.That(configuredFailureRate, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(1));

        var runs = 1000;
        var failures = 0;

        for (int i = 0; i < runs; i++)
        {
            var cmd = CreateChargeCommand(failureRate: configuredFailureRate);
            var result = _handler.Handle(cmd);
            if (!result.IsSuccess)
                failures++;
        }

        var actualFailureRate = (decimal)failures / runs;
        var deviation = Math.Abs(actualFailureRate - configuredFailureRate);

        // Assert: Within 5% margin
        Assert.That(deviation, Is.LessThan(0.05), 
            $"Configured: {configuredFailureRate}, Actual: {actualFailureRate}");
    }

    // Property 3.1.4: Amount Non-Negativity
    [Property]
    public void ChargePayment_NegativeAmount_IsRejected(decimal amount)
    {
        Assume.That(amount, Is.LessThanOrEqualTo(0));

        var command = new ChargePaymentCommand
        {
            OrderId = Guid.NewGuid(),
            Amount = amount,
            MessageId = Guid.NewGuid()
        };

        // Act & Assert
        Assert.Throws<ValidationException>(() => _handler.Handle(command));
    }
}

[TestFixture]
public class PaymentRefundPropertyTests
{
    // Property 3.2.1: Refund Idempotency
    [Property]
    public void RefundPayment_WithDuplicateMessageId_ProducesIdenticalRefund(
        Guid paymentId,
        decimal amount,
        Guid refundMessageId)
    {
        Assume.That(amount, Is.GreaterThan(0));

        // Arrange: Create charged payment
        var payment = _testDataGenerator.CreateChargedPayment(paymentId, amount);

        var command = new RefundPaymentCommand
        {
            PaymentId = paymentId,
            Amount = amount,
            MessageId = refundMessageId,
            Reason = "Saga compensation"
        };

        // Act
        var result1 = _handler.Handle(command);
        var refund1 = _dbContext.Refunds.First(r => r.PaymentId == paymentId);

        var result2 = _handler.Handle(command); // Replay
        var refund2 = _dbContext.Refunds.First(r => r.PaymentId == paymentId);

        // Assert
        Assert.That(result1.IsSuccess, Is.True);
        Assert.That(result2.IsSuccess, Is.True);
        Assert.That(result1.RefundId, Is.EqualTo(result2.RefundId));
        Assert.That(refund1.RefundId, Is.EqualTo(refund2.RefundId));
        
        // Verify only one refund record
        var refundCount = _dbContext.Refunds.Count(r => r.PaymentId == paymentId);
        Assert.That(refundCount, Is.EqualTo(1));
    }

    // Property 3.2.2: Charge-Refund Round Trip
    [Property]
    public void ChargeAndRefund_RoundTrip_PaymentStatusProgresses(
        Guid orderId,
        decimal amount)
    {
        Assume.That(amount, Is.GreaterThan(0));

        // Act: Charge
        var chargeCmd = new ChargePaymentCommand
        {
            OrderId = orderId,
            Amount = amount,
            MessageId = Guid.NewGuid(),
            FailureRate = 0.0
        };
        var chargeResult = _handler.Handle(chargeCmd);
        var payment = _dbContext.Payments.First(p => p.OrderId == orderId);

        // Refund
        var refundCmd = new RefundPaymentCommand
        {
            PaymentId = payment.PaymentId,
            Amount = amount,
            MessageId = Guid.NewGuid()
        };
        var refundResult = _handler.Handle(refundCmd);
        var refundedPayment = _dbContext.Payments.First(p => p.PaymentId == payment.PaymentId);

        // Assert
        Assert.That(payment.Status, Is.EqualTo("Charged"));
        Assert.That(refundedPayment.Status, Is.EqualTo("Refunded"));
    }

    // Property 3.2.3: Refund Validation
    [Property]
    public void RefundPayment_NonExistentPayment_IsRejected(Guid nonExistentPaymentId)
    {
        var command = new RefundPaymentCommand
        {
            PaymentId = nonExistentPaymentId,
            Amount = 100,
            MessageId = Guid.NewGuid()
        };

        // Act & Assert
        Assert.Throws<ValidationException>(() => _handler.Handle(command));
    }
}
```

### Configurable Failure Injection

```csharp
public interface IPaymentFailureInjector
{
    bool ShouldFail(decimal configuredFailureRate);
}

public class ConfigurablePaymentFailureInjector : IPaymentFailureInjector
{
    private readonly Random _random;
    private readonly ILogger<ConfigurablePaymentFailureInjector> _logger;

    public ConfigurablePaymentFailureInjector(string? seed = null, ILogger? logger = null)
    {
        // For reproducible testing
        _random = seed != null ? new Random(int.Parse(seed)) : new Random();
        _logger = logger;
    }

    public bool ShouldFail(decimal failureRate)
    {
        var shouldFail = _random.NextDouble() < (double)failureRate;
        _logger?.LogInformation($"Charge attempt: failureRate={failureRate}, shouldFail={shouldFail}");
        return shouldFail;
    }
}
```

### Prototype Milestone: Payment Service v0.1

**Acceptance Gate**: All property-based tests pass + Testcontainers integration

**Deliverables**:
- [ ] EF Core DbContext + Migrations
- [ ] Inbox Pattern implementation
- [ ] Configurable failure injector
- [ ] MassTransit Consumer (ChargePayment, RefundPayment commands)
- [ ] MassTransit Producer (PaymentCharged, PaymentFailed, PaymentRefunded events)
- [ ] Correlation ID propagation
- [ ] Structured logging
- [ ] All property-based tests passing

---

## Module 4: Notification Service

### Design Constraints

- **Pure Event Subscriber**: Only consumes events, never publishes events (no outbound coupling)
- **Simulated Delivery**: Logs notifications; does not send real emails/SMS
- **Retry Logic**: Exponential backoff with max retries before DLQ
- **Delivery Idempotency**: Multiple identical events produce single notification

### Component Architecture

```
Notification Service (ASP.NET Core)
│
├─ Application Layer
│  ├─ EventConsumers (OrderConfirmed, OrderFailed, OrderPlaced, etc.)
│  ├─ NotificationGenerator (Creates notification records)
│  └─ DeliveryExecutor (Simulates delivery, logs result)
│
├─ Domain Layer
│  ├─ Notification Aggregate
│  │  ├─ NotificationId (PK)
│  │  ├─ OrderId, CustomerId
│  │  ├─ EventType (OrderPlaced|OrderConfirmed|OrderFailed|...)
│  │  ├─ Message, Reason
│  │  ├─ Status (Pending|Delivered|Failed|DLQ)
│  │  ├─ RetryCount
│  │  ├─ LastError
│  │  └─ CreatedAt, LastAttemptAt
│  │
│  └─ NotificationRetryPolicy
│     ├─ MaxRetries (3)
│     ├─ InitialBackoff (1s)
│     ├─ BackoffMultiplier (2.0 — exponential)
│     └─ MaxBackoff (30s)
│
├─ Persistence Layer (EF Core)
│  ├─ NotificationDbContext
│  │  ├─ Notifications (DbSet)
│  │  ├─ NotificationHistory (DbSet) — immutable delivery log
│  │  └─ InboxMessages (DbSet) — idempotency store
│  │
│  └─ Migrations
│
├─ Messaging Layer (MassTransit + Kafka)
│  ├─ Consumer: ConsumeOrderConfirmedEvent
│  ├─ Consumer: ConsumeOrderFailedEvent
│  ├─ Consumer: ConsumeOrderPlacedEvent
│  └─ Consumer Error Handler (DLQ routing)
│
├─ Retry Engine
│  └─ BackgroundJob: NotificationRetryJob
│     ├─ Query failed notifications
│     ├─ Calculate backoff delay
│     ├─ Attempt delivery
│     ├─ On success: Mark delivered
│     └─ On max retries: Route to DLQ
│
└─ Observability
   ├─ Correlation ID Extraction/Propagation
   ├─ Structured Logging (Serilog)
   └─ OTel Spans
```

### Data Model

```sql
-- Notifications table
CREATE TABLE Notifications (
    NotificationId UUID PRIMARY KEY,
    OrderId UUID NOT NULL,
    CustomerId UUID NOT NULL,
    EventType VARCHAR(50) NOT NULL,
    Message TEXT NOT NULL,
    Reason VARCHAR(255),
    Status VARCHAR(20) NOT NULL CHECK (Status IN ('Pending', 'Delivered', 'Failed', 'DLQ')),
    RetryCount INT NOT NULL DEFAULT 0,
    LastError TEXT,
    MessageId UUID NOT NULL UNIQUE,
    CreatedAt TIMESTAMPTZ NOT NULL,
    LastAttemptAt TIMESTAMPTZ,
    DeliveredAt TIMESTAMPTZ,
    CorrelationId UUID NOT NULL
);

-- NotificationHistory table (immutable delivery log)
CREATE TABLE NotificationHistory (
    HistoryId UUID PRIMARY KEY,
    NotificationId UUID NOT NULL REFERENCES Notifications(NotificationId),
    Attempt INT NOT NULL,
    Result VARCHAR(20) NOT NULL CHECK (Result IN ('Success', 'Failed', 'Timeout')),
    ErrorMessage TEXT,
    AttemptedAt TIMESTAMPTZ NOT NULL
);

-- InboxMessages table (idempotency store)
CREATE TABLE InboxMessages (
    MessageId UUID PRIMARY KEY,
    OrderId UUID NOT NULL,
    MessageType VARCHAR(100) NOT NULL,
    Payload TEXT NOT NULL,
    ProcessedAt TIMESTAMPTZ NOT NULL,
    CorrelationId UUID NOT NULL
);
```

### Property-Based Test Suite

```csharp
[TestFixture]
public class NotificationIdempotencyPropertyTests
{
    // Property 4.1.1: Idempotent Event Consumption
    [Property]
    public void ConsumeOrderConfirmedEvent_DuplicateEvent_ProducesOneNotification(
        Guid orderId,
        Guid customerId,
        Guid messageId)
    {
        var evt = new OrderConfirmedEvent
        {
            OrderId = orderId,
            CustomerId = customerId,
            MessageId = messageId,
            CorrelationId = Guid.NewGuid()
        };

        // Act
        _consumer.Consume(evt);
        var notificationsAfterFirst = _dbContext.Notifications
            .Count(n => n.OrderId == orderId);

        _consumer.Consume(evt); // Replay
        var notificationsAfterSecond = _dbContext.Notifications
            .Count(n => n.OrderId == orderId);

        // Assert
        Assert.That(notificationsAfterFirst, Is.EqualTo(1));
        Assert.That(notificationsAfterSecond, Is.EqualTo(1));
    }

    // Property 4.1.2: Event-to-Notification Correspondence
    [Property]
    public void GenerateNotification_FromEvent_MaintainsEventProperties(
        OrderConfirmedEvent evt)
    {
        // Act
        var notification = _generator.Generate(evt);

        // Assert
        Assert.That(notification.OrderId, Is.EqualTo(evt.OrderId));
        Assert.That(notification.CustomerId, Is.EqualTo(evt.CustomerId));
        Assert.That(notification.EventType, Is.EqualTo("OrderConfirmed"));
    }

    // Property 4.1.3: Correlation_ID Propagation
    [Property]
    public void Notification_CorrelationId_MatchesSourceEvent(
        Guid correlationId,
        OrderConfirmedEvent evt)
    {
        evt.CorrelationId = correlationId;

        var notification = _generator.Generate(evt);

        Assert.That(notification.CorrelationId, Is.EqualTo(correlationId));
    }
}

[TestFixture]
public class NotificationRetryPropertyTests
{
    // Property 4.2.2: Retry Count Monotonicity
    [Property]
    public void Retry_AttemptCount_IncreasesMonotonically(
        Guid notificationId)
    {
        var notification = _testDataGenerator.CreateFailedNotification(notificationId);
        var initialRetryCount = notification.RetryCount;

        // Act: Simulate failed delivery
        _retryEngine.AttemptDelivery(notification);

        notification = _dbContext.Notifications.First(n => n.NotificationId == notificationId);

        // Assert
        Assert.That(notification.RetryCount, Is.EqualTo(initialRetryCount + 1));
    }

    // Property 4.2.3: Backoff Timing
    [Property]
    public void Retry_BackoffDelay_FollowsExponentialProgression(int attemptNumber)
    {
        Assume.That(attemptNumber, Is.GreaterThan(0).And.LessThan(10));

        var baseDelay = TimeSpan.FromSeconds(1);
        var expectedDelay = baseDelay.Multiply(Math.Pow(2, attemptNumber - 1));

        var actualDelay = _retryPolicy.CalculateBackoff(attemptNumber);

        Assert.That(
            actualDelay >= expectedDelay && actualDelay < expectedDelay.Multiply(1.5),
            $"Expected ~{expectedDelay.TotalSeconds}s, got {actualDelay.TotalSeconds}s"
        );
    }

    // Property 4.2.4: DLQ Routing on Exhaustion
    [Property]
    public void Notification_ExhaustedRetries_RoutedToDLQ(Guid notificationId)
    {
        var notification = _testDataGenerator.CreateNotification(notificationId);
        notification.RetryCount = 3; // Max retries

        // Act
        _retryEngine.ProcessNotification(notification);

        // Assert
        var dlqNotification = _dbContext.Notifications
            .First(n => n.NotificationId == notificationId);
        Assert.That(dlqNotification.Status, Is.EqualTo("DLQ"));
    }
}
```

### Prototype Milestone: Notification Service v0.1

**Acceptance Gate**: All property-based tests pass + Testcontainers integration

**Deliverables**:
- [ ] EF Core DbContext + Migrations
- [ ] Inbox Pattern implementation
- [ ] MassTransit Consumers (OrderPlaced, OrderConfirmed, OrderFailed events)
- [ ] Notification generation and simulated delivery
- [ ] Retry engine with exponential backoff
- [ ] Correlation ID propagation
- [ ] Structured logging
- [ ] All property-based tests passing

---

## Module 5: Saga Orchestrator

This is the most complex module. The state machine must handle the happy path and all failure/compensation paths.

### Design Constraints

- **Explicit State Machine**: All states and transitions persist to DB before command issuance (write-ahead)
- **Optimistic Concurrency**: Saga state versioned to handle concurrent updates
- **Compensating Transactions**: Reverse order of completed steps on failure
- **Timeout Handling**: Non-terminal sagas mark timeout after threshold, attempt compensation or escalate

### Component Architecture

```
Saga Orchestrator (ASP.NET Core + MassTransit Saga)
│
├─ State Machine Definition
│  ├─ States: Start → ReservingInventory → ChargingPayment → Completed
│  │                                    ↓
│  │                               (Failure) → Compensating → Failed
│  │
│  ├─ Events Trigger: OrderPlaced, InventoryReserved, InventoryRejected,
│  │                  PaymentCharged, PaymentFailed, InventoryReleased
│  │
│  └─ Commands Issued: ReserveInventory, ChargePayment, ReleaseInventory (compensation)
│
├─ Saga Repository (State Persistence)
│  ├─ SagaState Entity (persisted in SAGADB)
│  │  ├─ SagaId, OrderId
│  │  ├─ CurrentState (enum)
│  │  ├─ CompletedSteps (List<string>)
│  │  ├─ FailureReason (nullable)
│  │  ├─ RetryCount (per step)
│  │  ├─ CreatedAt, UpdatedAt
│  │  └─ Version (optimistic concurrency token)
│  │
│  └─ SagaStateTransition Entity (audit)
│     ├─ SagaId, OrderId
│     ├─ FromState, ToState
│     ├─ TriggeringEvent
│     ├─ CommandIssued
│     └─ Timestamp
│
├─ Command Issuance
│  ├─ ReserveInventoryCommand (→ Inventory.commands topic)
│  ├─ ChargePaymentCommand (→ payment.commands topic)
│  ├─ ReleaseInventoryCommand (→ inventory.commands topic, compensation)
│  └─ ConfirmOrderCommand (→ order.commands topic)
│
├─ Event Consumption
│  ├─ OrderPlacedEvent → Initialize saga
│  ├─ InventoryReserved → Advance state
│  ├─ InventoryRejected → Fail saga (no compensation needed)
│  ├─ PaymentCharged → Advance state
│  ├─ PaymentFailed → Trigger compensation
│  └─ InventoryReleased → Mark compensation complete, finalize saga
│
├─ Compensation Logic
│  └─ On PaymentFailed (or any step failure):
│     1. Persist state change to "Compensating"
│     2. Determine which steps need reversal
│     3. Issue compensation commands in reverse order
│     4. Mark saga "Failed" when all compensations complete
│
├─ Timeout Detection
│  └─ Background Job: SagaTimeoutMonitor
│     ├─ Query sagas in non-terminal states
│     ├─ If age > timeout threshold:
│     │  ├─ Log timeout
│     │  ├─ Trigger compensation if mid-flow
│     │  └─ Mark as "TimedOut"
│     └─ Escalate to manual review queue
│
└─ Query Endpoint
   └─ GET /api/admin/sagas/{id}
      └─ Return full saga state and transition history
```

### Data Model

```sql
-- SagaState table (MassTransit Saga State)
CREATE TABLE SagaState (
    SagaId UUID PRIMARY KEY,
    OrderId UUID NOT NULL UNIQUE,
    CurrentState VARCHAR(50) NOT NULL,
    CompletedSteps TEXT NOT NULL, -- JSON array of step names
    FailureReason VARCHAR(255),
    RetryCount INT NOT NULL DEFAULT 0,
    CreatedAt TIMESTAMPTZ NOT NULL,
    UpdatedAt TIMESTAMPTZ NOT NULL,
    Version INT NOT NULL (optimistic concurrency token)
);

-- SagaStateTransition table (audit trail)
CREATE TABLE SagaStateTransition (
    TransitionId UUID PRIMARY KEY,
    SagaId UUID NOT NULL REFERENCES SagaState(SagaId),
    OrderId UUID NOT NULL,
    FromState VARCHAR(50),
    ToState VARCHAR(50) NOT NULL,
    TriggeringEvent VARCHAR(100),
    CommandIssued VARCHAR(100),
    Timestamp TIMESTAMPTZ NOT NULL,
    CorrelationId UUID NOT NULL
);
```

### Property-Based Test Suite

```csharp
[TestFixture]
public class SagaStateMachinePropertyTests
{
    // Property 5.1.1: State Progression Validity
    [Property]
    public void StateMachine_Transitions_FollowValidPaths(
        List<SagaEvent> eventSequence)
    {
        // Generate events that should create valid state transitions
        var validTransitions = new Dictionary<string, string>
        {
            { "Start", "ReservingInventory" },
            { "ReservingInventory", "ChargingPayment" },
            { "ChargingPayment", "Completed" }
        };

        var saga = new OrderSaga();
        var currentState = "Start";

        foreach (var evt in eventSequence)
        {
            if (validTransitions.TryGetValue(currentState, out var nextState))
            {
                _machine.Fire(evt);
                currentState = nextState;
            }
        }

        // Assert: Final state is valid
        Assert.That(
            currentState,
            Is.AnyOf("Completed", "Failed", "Compensating")
        );
    }

    // Property 5.1.3: State Determinism
    [Property]
    public void ReplaySagaEvents_ProducesIdenticalState(
        List<SagaEvent> eventSequence)
    {
        var saga1 = new OrderSaga();
        var saga2 = new OrderSaga();

        foreach (var evt in eventSequence)
        {
            saga1.FireEvent(evt);
            saga2.FireEvent(evt);
        }

        Assert.That(saga1.CurrentState, Is.EqualTo(saga2.CurrentState));
        Assert.That(saga1.CompletedSteps, Is.EquivalentTo(saga2.CompletedSteps));
    }
}

[TestFixture]
public class SagaCompensationPropertyTests
{
    // Property 5.2.1: Compensation Completeness
    [Property]
    public void PaymentFailure_TriggersCompensation_ForAllCompletedSteps(
        List<SagaStep> completedSteps)
    {
        var saga = _testDataGenerator.CreateSagaWithCompletedSteps(completedSteps);

        // Act: Trigger payment failure
        _orchestrator.HandlePaymentFailedEvent(saga);

        // Assert: All completed steps have compensation
        foreach (var step in completedSteps)
        {
            var compensation = _dbContext.SagaCompensations
                .FirstOrDefault(c => c.SagaId == saga.SagaId && c.CompensatesStep == step.StepName);
            Assert.That(compensation, Is.Not.Null);
        }
    }

    // Property 5.2.2: Compensation Order Reversal
    [Property]
    public void CompensationOrder_IsReverseOfCompletedSteps(
        List<SagaStep> completedSteps)
    {
        Assume.That(completedSteps.Count, Is.GreaterThan(1));

        var saga = _testDataGenerator.CreateSagaWithCompletedSteps(completedSteps);

        // Act
        var compensationOrder = _orchestrator.DetermineCompensationOrder(saga);

        // Assert: Compensation order is reverse of completion order
        for (int i = 0; i < completedSteps.Count; i++)
        {
            Assert.That(
                compensationOrder[i],
                Is.EqualTo(completedSteps[completedSteps.Count - 1 - i])
            );
        }
    }

    // Property 5.2.3: Idempotent Compensation
    [Property]
    public void ExecuteCompensation_Multiple Times_ProducesSameResult(
        OrderSaga saga)
    {
        // Act
        var result1 = _orchestrator.ExecuteCompensation(saga);
        var result2 = _orchestrator.ExecuteCompensation(saga);

        // Assert
        Assert.That(result1.CompensationId, Is.EqualTo(result2.CompensationId));
        Assert.That(result1.Status, Is.EqualTo(result2.Status));
    }
}

[TestFixture]
public class SagaTimeoutPropertyTests
{
    // Property 5.3.1: Timeout Detection
    [Property]
    public void SagaInNonTerminalState_AgeExceedsThreshold_MarkedTimedOut(
        TimeSpan age)
    {
        Assume.That(age, Is.GreaterThan(TimeSpan.FromSeconds(60)));

        var saga = _testDataGenerator.CreateSagaWithAge(age, "ChargingPayment");

        // Act
        _timeoutMonitor.CheckForTimeouts();

        // Assert
        var timedOutSaga = _dbContext.SagaState
            .First(s => s.SagaId == saga.SagaId);
        Assert.That(timedOutSaga.CurrentState, Is.EqualTo("TimedOut"));
    }

    // Property 5.3.3: Recovery Idempotency
    [Property]
    public void ResumeSagaFromTimeout_MultipleCalls_ProduceConsistentState(
        OrderSaga timedOutSaga)
    {
        // Act
        var recovery1 = _orchestrator.ResumeSagaFromTimeout(timedOutSaga);
        var recovery2 = _orchestrator.ResumeSagaFromTimeout(timedOutSaga);

        // Assert
        Assert.That(recovery1.SagaId, Is.EqualTo(recovery2.SagaId));
        Assert.That(recovery1.FinalState, Is.EqualTo(recovery2.FinalState));
    }
}
```

### Prototype Milestone: Saga Orchestrator v0.1

**Acceptance Gate**: All property-based tests pass + integration tests with all consumer services

**Deliverables**:
- [ ] MassTransit State Machine definition
- [ ] SagaState persistence (EF Core)
- [ ] Event consumers (OrderPlaced, InventoryReserved, etc.)
- [ ] Command issuance (ReserveInventory, ChargePayment, etc.)
- [ ] Compensation logic (reverse order execution)
- [ ] Timeout detection and handling
- [ ] Query endpoint for admin dashboard
- [ ] Correlation ID propagation
- [ ] Structured logging
- [ ] All property-based tests passing

---

## Module 6: Kafka Layer

### Design Constraints

- **At-Least-Once Delivery**: Guaranteed by Kafka broker persistence and consumer offset management
- **Message ID Idempotency**: Every message carries unique MessageId; consumers deduplicate
- **Topic Partitioning**: Messages keyed by OrderId to preserve order per order
- **Dead Letter Queue**: Failed messages routed to `.dlq` topics

### Component Architecture (Shared Across All Services)

```
Kafka Integration Layer
│
├─ Producer Wrapper (MassTransit)
│  ├─ Embed MessageId in headers
│  ├─ Embed Correlation ID in headers
│  ├─ Embed Trace Context (W3C traceparent) in headers
│  ├─ Wait for broker ACK before returning
│  ├─ On publish failure: Throw exception (caller decides retry)
│  └─ Metrics: Publish latency, failure count
│
├─ Consumer Wrapper (MassTransit)
│  ├─ Extract MessageId from headers
│  ├─ Check InboxMessages table (consumer's DB)
│  │  ├─ If found: Skip processing, ACK
│  │  └─ If not found: Continue to business logic
│  │
│  ├─ Execute business logic (handler)
│  │  ├─ On success: Write inbox record + commit offset
│  │  └─ On transient error: Retry (bounded)
│  │
│  ├─ On max retries exhausted: Publish to DLQ
│  ├─ On poison message: Fast-track to DLQ
│  └─ Metrics: Processing latency, DLQ routes, retry counts
│
├─ DLQ Handler
│  ├─ Receive failed message from {topic}.dlq
│  ├─ Store with error context and original payload
│  ├─ Expose via admin API for manual reprocessing
│  └─ Log for alerting
│
├─ Topic Configuration
│  ├─ order.events: 3 partitions (dev)
│  ├─ inventory.commands: 3 partitions (dev)
│  ├─ inventory.events: 3 partitions (dev)
│  ├─ payment.commands: 3 partitions (dev)
│  ├─ payment.events: 3 partitions (dev)
│  ├─ order.commands: 3 partitions (dev)
│  ├─ notification.events: 3 partitions (dev)
│  └─ {topic}.dlq: 1 partition (dev)
│
└─ Consumer Group Strategy
   └─ One consumer group per service per topic:
      ├─ saga-orchestrator-group → inventory.events, payment.events
      ├─ inventory-service-group → inventory.commands
      ├─ payment-service-group → payment.commands
      ├─ order-service-group → order.commands
      └─ notification-service-group → notification.events
```

### Property-Based Test Suite (Integration Tests)

**Test File**: `Kafka.Tests/KafkaLayerPropertyTests.cs`

```csharp
[TestFixture]
public class KafkaProducerPropertyTests
{
    // Property 6.1.1: Delivery Guarantee
    [Property]
    public void PublishMessage_SuccessfulAck_MessageExistsOnBroker(
        KafkaMessage message)
    {
        // Act
        var result = _producer.PublishAsync(message).Result;

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        
        // Verify message exists by consuming
        var consumed = _consumer.ConsumeAsync(message.Topic).Result;
        Assert.That(consumed.MessageId, Is.EqualTo(message.MessageId));
    }

    // Property 6.1.2: Message_ID Preservation
    [Property]
    public void PublishAndConsume_MessageId_PreservedThroughCycle(
        Guid messageId,
        string payload)
    {
        var message = new KafkaMessage 
        { 
            MessageId = messageId, 
            Payload = payload 
        };

        // Act
        _producer.PublishAsync(message);
        var consumed = _consumer.ConsumeAsync(message.Topic).Result;

        // Assert
        Assert.That(consumed.Headers["MessageId"], Is.EqualTo(messageId.ToString()));
    }

    // Property 6.1.3: Partition Consistency
    [Property]
    public void MessagesWithSameKey_RoutedToSamePartition(
        Guid orderId,
        List<KafkaMessage> messages)
    {
        Assume.That(messages.Count, Is.GreaterThan(1));

        var partitions = new HashSet<int>();

        // Act
        foreach (var msg in messages)
        {
            msg.Key = orderId;
            _producer.PublishAsync(msg).Wait();
            var partition = _producer.GetPartition(msg);
            partitions.Add(partition);
        }

        // Assert: All messages for same key routed to same partition
        Assert.That(partitions.Count, Is.EqualTo(1));
    }
}

[TestFixture]
public class KafkaConsumerIdempotencyPropertyTests
{
    // Property 6.2.1: Duplicate Suppression
    [Property]
    public void ConsumeMessage_DuplicateMessageId_ProcessedOnlyOnce(
        Guid messageId,
        string payload,
        int duplicateCount)
    {
        Assume.That(duplicateCount, Is.GreaterThan(1));

        // Act: Publish same message N times
        for (int i = 0; i < duplicateCount; i++)
        {
            var msg = new KafkaMessage { MessageId = messageId, Payload = payload };
            _producer.PublishAsync(msg).Wait();
        }

        // Consume and deduplicate
        var results = _consumer.ConsumeAsync(topic: "test.topic", maxMessages: duplicateCount).Result;
        var processedCount = results.Count(r => r.MessageId == messageId && !r.IsSkipped);

        // Assert
        Assert.That(processedCount, Is.EqualTo(1));
    }

    // Property 6.2.3: Offset Commit Atomicity
    [Property]
    public void OffsetCommit_AfterProcessing_PreventsReplay(
        Guid messageId,
        string payload)
    {
        var message = new KafkaMessage { MessageId = messageId, Payload = payload };

        // Act: Publish, consume, commit
        _producer.PublishAsync(message).Wait();
        var result1 = _consumer.ConsumeAsync(topic: "test.topic").Result;
        _consumer.CommitAsync().Wait();

        // Simulate crash and recovery
        _consumer = new KafkaConsumer(_configuration); // New consumer instance
        var result2 = _consumer.ConsumeAsync(topic: "test.topic").Result;

        // Assert: Message not redelivered after offset commit
        Assert.That(result2, Is.Empty);
    }
}

[TestFixture]
public class KafkaDLQPropertyTests
{
    // Property 6.3.1: DLQ Routing on Exhaustion
    [Property]
    public void FailedMessage_AfterMaxRetries_RoutedToDLQ(
        Guid messageId,
        string payload)
    {
        // Inject handler that always throws
        var handler = new FailingMessageHandler();
        
        var message = new KafkaMessage { MessageId = messageId, Payload = payload };

        // Act
        _producer.PublishAsync(message).Wait();
        
        // Consume with failing handler
        for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
        {
            try { handler.Handle(message); }
            catch { /* Expected to fail */ }
        }

        // Check DLQ
        var dlqMessages = _dlqConsumer.ConsumeAsync(topic: "test.topic.dlq").Result;

        // Assert
        var dlqMessage = dlqMessages.FirstOrDefault(m => m.MessageId == messageId);
        Assert.That(dlqMessage, Is.Not.Null);
        Assert.That(dlqMessage.Headers["RetryCount"], Is.EqualTo(MAX_RETRIES.ToString()));
    }

    // Property 6.3.2: DLQ Error Context Preservation
    [Property]
    public void DLQMessage_IncludesErrorContext(
        Guid messageId,
        Exception originalException)
    {
        var message = new KafkaMessage { MessageId = messageId };
        
        // Act
        var dlqMessage = _dlqService.CreateDLQMessage(message, originalException);

        // Assert
        Assert.That(dlqMessage.Headers["OriginalException"], Is.Not.Null);
        Assert.That(dlqMessage.Headers["StackTrace"], Contains.Substring("at "));
    }
}
```

### Prototype Milestone: Kafka Layer v0.1

**Acceptance Gate**: All property-based tests pass + Testcontainers with real Kafka broker

**Deliverables**:
- [ ] Producer wrapper (MessageId injection, ACK wait)
- [ ] Consumer wrapper (Inbox pattern, deduplication)
- [ ] DLQ routing logic
- [ ] Testcontainers setup (KafkaContainer)
- [ ] Topic and consumer group creation
- [ ] All property-based tests passing
- [ ] Metrics instrumentation

---

## Module 7: API Gateway (YARP)

### Design Constraints

- **Single Ingress**: Only public-facing surface; all service-to-service communication via Kafka
- **Routing**: Route-based forwarding to backend services
- **Rate Limiting**: Fixed-window per consumer (API key or IP)
- **Circuit Breaking**: Polly policy on backend failures

### Prototype Milestone: API Gateway v0.1

**Acceptance Gate**: All circuit breaker and rate limiting tests pass

**Deliverables**:
- [ ] YARP proxy configuration
- [ ] Route table (orders, catalog, admin endpoints)
- [ ] Rate limiter (fixed-window per consumer)
- [ ] Circuit breaker (Polly integration)
- [ ] Trace context propagation (correlation ID in headers)
- [ ] All property-based tests passing

---

## Module 8: Observability

### Design Constraints

- **Correlation ID Propagation**: Every request/event carries Correlation ID end-to-end
- **Trace Context Across Kafka**: Explicitly inject/extract W3C traceparent in Kafka headers
- **Structured Logging**: JSON logs with trace ID, span ID, correlation ID
- **Metrics**: Service latencies, error rates, DLQ sizes tagged with Correlation ID

### Prototype Milestone: Observability v0.1

**Acceptance Gate**: All trace propagation tests pass

**Deliverables**:
- [ ] Correlation ID generation and propagation middleware
- [ ] W3C traceparent handling in Kafka producer/consumer
- [ ] Serilog structured logging configuration
- [ ] OTel SDK integration (HTTP, EF Core, Kafka spans)
- [ ] Jaeger exporter configuration
- [ ] Metrics collection (latencies, errors)
- [ ] All property-based tests passing

---

## Implementation Roadmap with Test Gates

### Phase 0: Foundations (Days 1–2)

**Gate**: Docker Compose spins up, services can publish/consume on Kafka

**Tasks**:
1. Create solution structure (one project per service)
2. Set up Docker Compose (Kafka KRaft, Postgres ×5, Redis, Jaeger)
3. Install MassTransit + Kafka transport
4. Install EF Core + Testcontainers
5. Create base shared contracts library (DTOs)

**Deliverables**:
- docker-compose.yml
- Global.sln with 8 projects
- Shared contracts library

### Phase 1: Order Service (Days 3–5)

**Gate**: All Order Service properties pass; can place order and query status

**Tasks**:
1. Design Order entity and DbContext (Requirement 1.1, 1.2)
2. Implement place order HTTP endpoint
3. Implement query status HTTP endpoint
4. Implement Inbox pattern
5. Write 100% of property-based tests (§ Module 1)
6. Verify all properties pass
7. Build prototype: HTTP API only (no messaging yet)

**Tests to Pass**:
- [ ] Idempotent Order Creation (Property 1.1.1)
- [ ] Order State Determinism (Property 1.1.2)
- [ ] Event-State Correspondence (Property 1.1.3)
- [ ] Message_ID Uniqueness (Property 1.1.4)
- [ ] Query Result Consistency (Property 1.2.1)
- [ ] Status Progression Validity (Property 1.2.2)
- [ ] Timestamp Monotonicity (Property 1.2.3)
- [ ] Correlation_ID Presence (Property 1.3.1)
- [ ] Correlation_ID Immutability (Property 1.3.2)

### Phase 2: Inventory Service (Days 6–8)

**Gate**: All Inventory properties pass; reserve/release stock with idempotency, Redis cache works

**Tasks**:
1. Design Reservation Ledger (Requirement 2.1, 2.2)
2. Implement reservation logic with Inbox pattern
3. Implement release logic (compensation)
4. Implement Redis cache-aside (Requirement 2.3)
5. Write all property-based tests
6. Verify all properties pass
7. Build prototype

**Tests to Pass**:
- [ ] Idempotent Processing (Property 2.1.1)
- [ ] Ledger Immutability (Property 2.1.2)
- [ ] Stock Consistency (Property 2.1.3)
- [ ] Reservation Ledger Audit Trail (Property 2.1.4)
- [ ] Stock Never Negative (Property 2.1.5)
- [ ] Release Idempotency (Property 2.2.1)
- [ ] Reserve-Release Round Trip (Property 2.2.2)
- [ ] Ledger Debit-Credit Balance (Property 2.2.3)
- [ ] Release Validation (Property 2.2.4)
- [ ] Cache-Ledger Consistency (Property 2.3.1)
- [ ] Cache Rebuild Idempotency (Property 2.3.2)
- [ ] TTL Prevents Stale Reads (Property 2.3.3)
- [ ] Fallback Correctness (Property 2.3.4)

### Phase 3: Payment Service (Days 9–10)

**Gate**: All Payment properties pass; charge with configurable failure rate, refund idempotency

**Tasks**:
1. Design Payment entity and ledger (Requirement 3.1, 3.2)
2. Implement charge with configurable failure injection
3. Implement refund (compensation)
4. Write all property-based tests
5. Verify all properties pass
6. Build prototype

**Tests to Pass**:
- [ ] Idempotent Payment Processing (Property 3.1.1)
- [ ] Failure Rate Distribution (Property 3.1.2)
- [ ] Payment Record Immutability (Property 3.1.3)
- [ ] Amount Non-Negativity (Property 3.1.4)
- [ ] Refund Idempotency (Property 3.2.1)
- [ ] Charge-Refund Round Trip (Property 3.2.2)
- [ ] Refund Validation (Property 3.2.3)

### Phase 4: Kafka Layer (Days 11–12)

**Gate**: All Kafka properties pass; duplicate suppression, DLQ routing works

**Tasks**:
1. Implement producer wrapper (MessageId, Correlation ID, ACK wait)
2. Implement consumer wrapper (Inbox deduplication)
3. Implement DLQ routing
4. Create topics and consumer groups
5. Write all property-based tests (with Testcontainers Kafka)
6. Verify all properties pass
7. Build prototype (standalone, no service yet)

**Tests to Pass**:
- [ ] Delivery Guarantee (Property 6.1.1)
- [ ] Message_ID Preservation (Property 6.1.2)
- [ ] Partition Consistency (Property 6.1.3)
- [ ] Duplicate Suppression (Property 6.2.1)
- [ ] Idempotency Store Durability (Property 6.2.2)
- [ ] Offset Commit Atomicity (Property 6.2.3)
- [ ] Failed Processing Retry (Property 6.2.4)
- [ ] DLQ Routing on Exhaustion (Property 6.3.1)
- [ ] DLQ Error Context Preservation (Property 6.3.2)
- [ ] Poison Message Detection (Property 6.3.3)
- [ ] Offset Persistence During Rebalance (Property 6.4.1)
- [ ] Partition Assignment Completeness (Property 6.4.2)

### Phase 5: Notification Service (Days 13–14)

**Gate**: All Notification properties pass; event consumption idempotency, retry logic

**Tasks**:
1. Design Notification entity (Requirement 4.1, 4.2)
2. Implement event consumers (OrderPlaced, OrderConfirmed, OrderFailed)
3. Implement notification generation
4. Implement retry engine with exponential backoff
5. Write all property-based tests
6. Verify all properties pass
7. Build prototype

**Tests to Pass**:
- [ ] Idempotent Event Consumption (Property 4.1.1)
- [ ] Event-to-Notification Correspondence (Property 4.1.2)
- [ ] Correlation_ID Propagation (Property 4.1.3)
- [ ] Notification Status Progression (Property 4.1.4)
- [ ] Notification History Immutability (Property 4.2.1)
- [ ] Retry Count Monotonicity (Property 4.2.2)
- [ ] Backoff Timing (Property 4.2.3)
- [ ] DLQ Routing on Exhaustion (Property 4.2.4)

### Phase 6: Saga Orchestrator (Days 15–18) — CRITICAL

**Gate**: ALL Saga properties pass, including compensation path tests (this is the proof point)

**Tasks**:
1. Design state machine (Requirement 5.1, 5.2, 5.3)
2. Implement state machine with MassTransit Automatonymous
3. Implement command issuance (ReserveInventory, ChargePayment, ReleaseInventory)
4. **Implement compensation logic** (ReleaseInventory on payment failure)
5. Implement timeout detection and recovery
6. Write all property-based tests, including **compensation path forced tests**
7. **Verify ALL properties pass, especially compensation** (Property 5.2.1–5.2.5)
8. Build prototype

**Tests to Pass**:
- [ ] State Progression Validity (Property 5.1.1)
- [ ] SagaId Uniqueness (Property 5.1.2)
- [ ] State Determinism (Property 5.1.3)
- [ ] Event-State Correspondence (Property 5.1.4)
- [ ] **Compensation Completeness (Property 5.2.1)** ← CRITICAL
- [ ] **Compensation Order Reversal (Property 5.2.2)** ← CRITICAL
- [ ] **Idempotent Compensation (Property 5.2.3)** ← CRITICAL
- [ ] **Failed Step Triggers Compensation (Property 5.2.4)** ← CRITICAL
- [ ] **Ledger Balance After Compensation (Property 5.2.5)** ← CRITICAL
- [ ] Timeout Detection (Property 5.3.1)
- [ ] Timeout-Triggered Compensation (Property 5.3.2)
- [ ] Recovery Idempotency (Property 5.3.3)

**INTEGRATION TEST**: Force payment failure mid-saga
- Assert: Inventory is released (InventoryReleased event published)
- Assert: Order marked Failed (OrderFailed event published)
- Assert: Stock count returns to pre-reservation

### Phase 7: API Gateway (Days 19–20)

**Gate**: All routing, rate limiting, circuit breaker tests pass

**Tasks**:
1. Configure YARP routes
2. Implement rate limiter (fixed-window per API key)
3. Implement circuit breaker (Polly)
4. Write property-based tests
5. Verify all properties pass
6. Build prototype

### Phase 8: Observability (Days 21–22)

**Gate**: All trace propagation tests pass; can view complete order flow in Jaeger

**Tasks**:
1. Implement Correlation ID generation and propagation
2. Implement W3C traceparent handling in Kafka
3. Set up structured logging (Serilog)
4. Set up OTel SDK (HTTP, EF Core, Kafka spans)
5. Configure Jaeger exporter
6. Write property-based tests
7. Verify all properties pass
8. Build prototype

### Phase 9: Integration Testing (Days 23–25)

**Gate**: End-to-end saga tests pass (happy path + all compensation paths)

**Tasks**:
1. Create integration test suite (Testcontainers, real Kafka/Postgres/Redis)
2. Test happy path: order → reserve → charge → complete
3. **Test payment failure compensation**: order → reserve → charge fails → release → fail
4. **Test orchestrator crash recovery**: Kill orchestrator mid-saga, restart, assert resume
5. **Test duplicate message suppression**: Replay same message, assert no double effect
6. **Test DLQ routing**: Inject poison message, assert lands in DLQ
7. **Test concurrent sagas**: 100 concurrent orders, assert no partition blocking
8. Verify trace propagation in Jaeger for all scenarios

### Phase 10: UI + Polish (Days 26–27)

**Gate**: UI displays order progress, admin dashboard shows saga state

**Tasks**:
1. Build minimal checkout UI
2. Build order status dashboard (polling)
3. Build admin dashboard (saga state + transitions + Jaeger link)
4. Build chaos panel (dev-only: force failures, kill services)

### Phase 11: Documentation + Demo (Day 28)

**Deliverables**:
- [ ] Architecture diagram (updated from design phase)
- [ ] "How to Demo" guide showing each failure mode
- [ ] README with module descriptions
- [ ] Property-based test coverage report (show 100% properties passing)
- [ ] Video walkthrough of compensation scenario

---

## Test Artifact Organization

```
Distributed-Order-Management-System/
├─ src/
│  ├─ Orders.Service/
│  │  └─ (implementation)
│  ├─ Inventory.Service/
│  │  └─ (implementation)
│  ├─ Payment.Service/
│  │  └─ (implementation)
│  ├─ Notification.Service/
│  │  └─ (implementation)
│  ├─ Saga.Orchestrator/
│  │  └─ (implementation)
│  ├─ Gateway/
│  │  └─ (implementation)
│  └─ Contracts/
│     └─ (shared DTOs)
│
├─ tests/
│  ├─ Orders.Service.Tests/
│  │  ├─ OrderServicePropertyTests.cs (Property 1.1.1 – 1.3.2)
│  │  └─ OrderServiceIntegrationTests.cs
│  │
│  ├─ Inventory.Service.Tests/
│  │  ├─ InventoryPropertyTests.cs (Property 2.1.1 – 2.3.4)
│  │  └─ InventoryIntegrationTests.cs
│  │
│  ├─ Payment.Service.Tests/
│  │  ├─ PaymentPropertyTests.cs (Property 3.1.1 – 3.2.3)
│  │  └─ PaymentIntegrationTests.cs
│  │
│  ├─ Notification.Service.Tests/
│  │  ├─ NotificationPropertyTests.cs (Property 4.1.1 – 4.2.4)
│  │  └─ NotificationIntegrationTests.cs
│  │
│  ├─ Saga.Orchestrator.Tests/
│  │  ├─ SagaStateMachinePropertyTests.cs (Property 5.1.1 – 5.1.4)
│  │  ├─ SagaCompensationPropertyTests.cs (Property 5.2.1 – 5.2.5) ← CRITICAL
│  │  ├─ SagaTimeoutPropertyTests.cs (Property 5.3.1 – 5.3.3)
│  │  └─ SagaIntegrationTests.cs (End-to-end with all failure scenarios)
│  │
│  ├─ Kafka.Tests/
│  │  ├─ KafkaLayerPropertyTests.cs (Property 6.1.1 – 6.4.3)
│  │  └─ KafkaIntegrationTests.cs (Testcontainers)
│  │
│  ├─ Gateway.Tests/
│  │  ├─ GatewayPropertyTests.cs (Circuit breaker, rate limiting)
│  │  └─ GatewayIntegrationTests.cs
│  │
│  ├─ Observability.Tests/
│  │  ├─ TraceContextPropertyTests.cs (Property 8.1.1 – 8.1.4)
│  │  ├─ StructuredLoggingPropertyTests.cs (Property 8.2.1 – 8.2.4)
│  │  └─ MetricsPropertyTests.cs (Property 8.3.1 – 8.3.4)
│  │
│  └─ Integration.Tests/
│     ├─ EndToEndSagaTests.cs
│     │  ├─ HappyPath_Test()
│     │  ├─ PaymentFailure_Compensation_Test() ← CRITICAL
│     │  ├─ OrchestratorCrash_Recovery_Test() ← CRITICAL
│     │  ├─ DuplicateMessage_Suppression_Test()
│     │  ├─ PoisonMessage_DLQ_Test()
│     │  └─ ConcurrentOrders_NoPartitionBlocking_Test()
│     │
│     └─ ChaosTests.cs
│        ├─ KillBroker_Test()
│        ├─ KillOrchestrator_Test()
│        ├─ KillPaymentService_Test()
│        └─ NetworkPartition_Test()
│
├─ docker-compose.yml
├─ README.md (with "How to Demo" section)
└─ .kiro/
   └─ specs/
      └─ distributed-order-management-system/
         ├─ requirements.md
         ├─ design.md (this file)
         └─ tasks.md (phased implementation tasks)
```

---

## Definition of Done for Each Module

A module ships as a prototype only when:

1. ✅ All property-based tests written and passing (100% coverage of properties from requirements)
2. ✅ Testcontainers integration tests passing (real PostgreSQL, Redis, Kafka for that module)
3. ✅ Correlation ID propagated end-to-end
4. ✅ Structured logging (Serilog) configured
5. ✅ OTel spans instrumented
6. ✅ No hardcoded secrets
7. ✅ README with usage example
8. ✅ No warnings or errors in build
9. ✅ Code review passed (peer or reviewer)
10. ✅ Integration with dependent modules tested (e.g., Saga Orchestrator tested with all 4 services)

---

## Success Criteria for the Entire Project

**At the end of Day 28, the project is successful if:**

1. ✅ Every requirement from the Requirements Document has a passing property-based test
2. ✅ Every failure mode from the PRD (§3) has a passing automated test
3. ✅ A reviewer can kill any service mid-saga and the system self-heals (compensation executes, order fails cleanly)
4. ✅ Trace propagation across Kafka works end-to-end (one Correlation ID spans all 5 services in Jaeger)
5. ✅ All services export structured logs with Correlation ID
6. ✅ No cross-service database queries detected
7. ✅ Circuit breaker gracefully degrades (no cascading failures)
8. ✅ Duplicate message delivery is suppressed (idempotency works)
9. ✅ DLQ receives failed messages and stores error context
10. ✅ README includes "How to Demo" section with commands to force each failure mode

---

## Notes for Implementation

- **Do not mock Kafka**: Use Testcontainers with real Kafka broker for consumer tests. Mocking defeats the purpose of proving messaging behavior.
- **Do not skip compensation tests**: The compensation path is the core value of this project. Test it early and aggressively.
- **Do propagate trace context across Kafka**: This is easy to get wrong. Explicitly inject W3C traceparent in Kafka headers on produce and extract on consume.
- **Do enforce data isolation**: Use separate PostgreSQL instances per service, not schemas. This is a design guardrail, not optional.
- **Do version saga state**: Use optimistic concurrency tokens to detect concurrent updates and handle gracefully.
- **Do log failures**: Every failure (retry, timeout, compensation) should be logged with Correlation ID and context.

This document is ready for implementation. Begin with Phase 0 (Foundations) and advance each phase only when all property-based tests pass.

