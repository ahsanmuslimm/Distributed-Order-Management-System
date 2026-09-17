# Order Service

## Overview

The **Order Service** is the entry point for customer orders in the Distributed Order Management System. It owns all order records and orchestrates the saga pattern for order fulfillment.

**Responsibilities**:
- Accept incoming orders (POST /api/orders)
- Maintain order state (Pending → Reserved → Charged → Confirmed/Failed)
- Query order status (GET /api/orders/{id})
- Publish domain events (OrderPlaced, OrderConfirmed, OrderFailed)
- Consume commands from Saga Orchestrator (ConfirmOrder, FailOrder)
- Provide audit trail of all state transitions

**Key Pattern**: Inbox Pattern for idempotent message consumption (deduplication).

---

## Project Structure

```
Orders.Service/
├── Entities/
│   ├── Order.cs                    # Aggregate root
│   ├── OrderItem.cs                # Line items
│   ├── OrderStatus.cs              # Status enum
│   ├── OrderStatusTransition.cs    # Audit trail
│   └── InboxMessage.cs             # Idempotency store
├── Data/
│   ├── OrderDbContext.cs           # EF Core DbContext
│   └── Migrations/
│       ├── 20260917000001_InitialMigration.cs
│       └── OrderDbContextModelSnapshot.cs
├── Program.cs                      # Startup configuration
└── README.md                       # This file

Tests/
├── Orders.Service.Tests/
│   ├── OrderServicePropertyTests.cs
│   └── ...other test files
```

---

## Data Model

### Orders Table
```sql
CREATE TABLE Orders (
    OrderId UUID PRIMARY KEY,
    CustomerId UUID NOT NULL,
    Status VARCHAR(50) NOT NULL,
    TotalAmount NUMERIC(10,2),
    SagaId UUID REFERENCES SagaState(SagaId),
    CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    LastUpdatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    Version INT NOT NULL -- Optimistic concurrency
);
```

### OrderItems Table
```sql
CREATE TABLE OrderItems (
    OrderItemId UUID PRIMARY KEY,
    OrderId UUID NOT NULL REFERENCES Orders(OrderId) ON DELETE CASCADE,
    ProductId UUID NOT NULL,
    Quantity INT NOT NULL CHECK (Quantity > 0),
    UnitPrice NUMERIC(10,2)
);
```

### OrderStatusTransitions Table (Audit)
```sql
CREATE TABLE OrderStatusTransitions (
    TransitionId UUID PRIMARY KEY,
    OrderId UUID NOT NULL REFERENCES Orders(OrderId) ON DELETE CASCADE,
    FromStatus VARCHAR(50),
    ToStatus VARCHAR(50) NOT NULL,
    Reason VARCHAR(255),
    Timestamp TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    CorrelationId UUID NOT NULL
);
```

### InboxMessages Table (Idempotency)
```sql
CREATE TABLE InboxMessages (
    MessageId UUID PRIMARY KEY UNIQUE,
    OrderId UUID NOT NULL,
    MessageType VARCHAR(100) NOT NULL,
    Payload TEXT,
    ProcessedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    CorrelationId UUID NOT NULL
);
```

---

## Order Status State Machine

```
    ┌─ Pending
    │     ↓ (InventoryReserved event)
    ├─ Reserved
    │     ├─→ Charged (PaymentCharged event)
    │     │       ├─→ Confirmed (ConfirmOrder command)
    │     │       └─→ Failed (PaymentFailed → compensation)
    │     └─→ Failed (InventoryRejected event)
    │
    └─ Compensated (if saga recovery needed)
```

**Status Enum**:
- `Pending` (0): Order created, awaiting inventory check
- `Reserved` (1): Inventory confirmed, awaiting payment
- `Charged` (2): Payment successful, awaiting final confirmation
- `Confirmed` (3): Order complete, customer notified
- `Failed` (4): Order failed (inventory unavailable or payment declined)
- `Compensated` (5): Was confirmed but saga rolled back (rare)

---

## Entity Framework Migrations

### Create Database
```bash
dotnet ef database update --project src/Orders.Service
```

### Create New Migration (after schema changes)
```bash
dotnet ef migrations add <MigrationName> --project src/Orders.Service
```

### Remove Latest Migration
```bash
dotnet ef migrations remove --project src/Orders.Service
```

---

## Phase 1 Implementation Tasks

### ✅ Task 1.1: Order Entity & DbContext (COMPLETE)

**Deliverables**:
- [x] Order entity with properties: OrderId, CustomerId, Status, Items, TotalAmount, SagaId, CreatedAt, LastUpdatedAt, Version
- [x] OrderItem entity for line items
- [x] OrderStatusTransition entity for audit trail
- [x] InboxMessage entity for idempotency store (Inbox Pattern)
- [x] OrderDbContext configured with Fluent API
- [x] Indexes on CustomerId, Status, CreatedAt, SagaId, CorrelationId
- [x] Check constraints (Quantity > 0)
- [x] Initial migration: `20260917000001_InitialMigration.cs`
- [x] Migration snapshot file

**Property-Based Tests Implemented** (9 properties):
- [x] Property 1.1.1: Idempotent Order Creation (duplicate MessageIds)
- [x] Property 1.1.2: Order State Determinism (identical inputs → identical state)
- [x] Property 1.1.3: Event-State Correspondence (every transition audited)
- [x] Property 1.1.4: MessageId Uniqueness (no duplicates allowed)
- [x] Property 1.2.1: Query Result Consistency (no stale reads)
- [x] Property 1.2.2: Status Progression Validity (state machine rules)
- [x] Property 1.2.3: Timestamp Monotonicity (CreatedAt ≤ LastUpdatedAt)
- [x] Property 1.3.1: CorrelationId Presence (always present)
- [x] Property 1.3.2: CorrelationId Immutability (propagated, not replaced)

**Test Framework**: xUnit + FsCheck (property-based)

---

### 🔵 Task 1.2: Place Order HTTP Endpoint (NEXT)

**What This Requires**:
1. HTTP endpoint: `POST /api/orders`
2. Input DTO: OrderRequest (CustomerId, Items[])
3. Handler: PlaceOrderHandler (creates order, publishes OrderPlacedEvent)
4. Endpoint: PlaceOrderEndpoint (accepts HTTP request)
5. Response: 202 Accepted with OrderId

**Files to Create**:
- `Orders.Service/DTOs/OrderRequest.cs`
- `Orders.Service/DTOs/OrderResponse.cs`
- `Orders.Service/Handlers/PlaceOrderHandler.cs`
- `Orders.Service/Endpoints/PlaceOrderEndpoint.cs`

---

### 🔵 Task 1.3: Query Order Status Endpoint (NEXT)

**What This Requires**:
1. HTTP endpoint: `GET /api/orders/{id}`
2. Handler: GetOrderStatusHandler
3. Response: Order DTO with full state

---

### 🔵 Task 1.4: Inbox Pattern Implementation (NEXT)

**What This Requires**:
1. IInboxProcessor interface
2. InboxProcessor implementation (checks MessageId, atomically inserts)
3. Unit tests for duplicate handling

---

### 🔵 Task 1.5–1.8: Middleware, Logging, Tests, Release (NEXT)

---

## Database Connection String

**Environment Variable**:
```
ORDER_SERVICE_CONNECTION_STRING=User Id=postgres;Password=postgres;Host=localhost;Port=5432;Database=orders;
```

**In appsettings.json**:
```json
{
  "ConnectionStrings": {
    "OrderDb": "User Id=postgres;Password=postgres;Host=localhost;Port=5432;Database=orders;"
  }
}
```

---

## Inbox Pattern Explanation

The **Inbox Pattern** ensures idempotent message consumption:

1. **Incoming Message** arrives from Kafka with `MessageId`
2. **Check Inbox**: Query InboxMessages table for this MessageId
   - **If found**: Already processed → skip (return idempotent result)
   - **If not found**: Proceed to step 3
3. **Process Message**: Create order, transition status, etc.
4. **Atomically Record**: Insert MessageId into InboxMessages (same transaction)
   - If insert fails (duplicate): Message was processed concurrently → skip
5. **Success**: Message processed exactly once, despite at-least-once delivery

**Why This Matters**:
- Kafka guarantees at-least-once delivery (messages can be retried)
- Without Inbox Pattern: Duplicate delivery → duplicate effects (wrong!)
- With Inbox Pattern: Duplicate delivery → idempotent (correct!)

---

## Correlation ID Propagation

Every message carries a **CorrelationId** for distributed tracing:

1. **Incoming Event/Command** has CorrelationId in headers
2. **Extract** from Kafka headers (via middleware)
3. **Store** in AsyncLocal<> for application-wide access
4. **Propagate** to all outgoing events/commands
5. **Log** every message with CorrelationId
6. **Query** Jaeger with CorrelationId to see full trace

Example:
```
GET /orders/123?correlationId=abc-def  (extracted from header)
  → PlaceOrderHandler logs with abc-def
  → Publishes OrderPlacedEvent with abc-def
  → Saga receives event with abc-def
  → Issues ReserveInventory command with abc-def
  → Inventory Service logs with abc-def
  → One trace ID (abc-def) spans all services!
```

---

## Next Steps

1. **Task 1.2**: Implement Place Order endpoint
2. **Task 1.3**: Implement Query endpoint
3. **Task 1.4**: Implement Inbox Pattern processor
4. **Task 1.5**: Add correlation ID middleware
5. **Task 1.6**: Verify all 9 property tests pass
6. **Task 1.7**: Configure Serilog
7. **Task 1.8**: Tag v0.1 release

---

## References

- **Specification**: `.kiro/specs/distributed-order-management-system/design.md` (Module 1)
- **Tasks**: `.kiro/specs/distributed-order-management-system/tasks.md` (Phase 1)
- **Inbox Pattern**: https://microservices.io/patterns/messaging/idempotent-consumer.html
- **EF Core Docs**: https://learn.microsoft.com/en-us/ef/core/

---

**Status**: Phase 1, Task 1.1 ✅ Complete (Entities + DbContext + Migration + Property Tests)
