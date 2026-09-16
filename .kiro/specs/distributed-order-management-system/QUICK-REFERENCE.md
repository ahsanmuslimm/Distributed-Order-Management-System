# Quick Reference - Distributed Order Management System

## For the Impatient: TL;DR

**What**: Build a distributed order system that handles payment failures gracefully by automatically releasing inventory.

**When**: 28 days, 11 phases, ~100 tasks

**Why**: Demonstrate mastery of saga orchestration, idempotency, compensation, and distributed tracing

**How**: Follow tasks.md, write property-based tests first, verify with integration tests daily

**Success**: Run:
```bash
dotnet test --filter "Category=Critical"
```
Should see:
- ✅ PaymentFailure_Compensation_Test PASSED
- ✅ OrchestratorCrash_Recovery_Test PASSED

---

## 5-Minute Project Overview

```
┌─────────────────────────────────────────────────────────┐
│         Customer Places Order via Web UI                │
└──────────────────┬──────────────────────────────────────┘
                   │ HTTP
                   ▼
┌─────────────────────────────────────────────────────────┐
│           API Gateway (YARP) - Routes                   │
│        Rate Limiting + Circuit Breaking                 │
└──────────────────┬──────────────────────────────────────┘
                   │ HTTP
                   ▼
┌─────────────────────────────────────────────────────────┐
│          Order Service - Accepts Order                  │
│       Returns 202 Accepted + Order ID                   │
│  Publishes "OrderPlaced" event to Kafka                │
└──────────────────┬──────────────────────────────────────┘
                   │ Kafka Event
                   ▼
┌─────────────────────────────────────────────────────────┐
│    Saga Orchestrator - Coordinates Workflow            │
│  Current State Machine: Pending → Reserving →         │
│              Charging → Confirmed                       │
└────────┬──────────────────────────────┬────────┬────────┘
         │ Reserve Command              │        │
         ▼                              │        │
    ┌─────────────────┐               │        │
    │ Inventory       │               │        │ Charge Command
    │ Service         │──Event→ Orch──┤        ▼
    │ (Reserve Stock) │               │   ┌─────────────┐
    └─────────────────┘               │   │ Payment     │
                                      │   │ Service     │
                                      │   │ (CHARGE)    │
                                      │   └─────────────┘
                                      │
                           FAILURE ───┴──→ Release Inventory (Compensation)
                                           ↓ Automatic, no manual steps
                                           Order marked FAILED
                                           ↓
                                      Notification Service
                                      (Customer notified)
```

**Key Point**: If payment fails, system automatically executes `ReleaseInventory` command. Inventory is freed. No manual database fixes required.

---

## Which Document Do I Read?

| I want to... | Read... | Time |
|---|---|---|
| Understand the overall project | README.md | 15 min |
| Know what each module must do | requirements.md | 30 min |
| Understand how to build it | design.md | 40 min |
| Follow implementation step-by-step | tasks.md | Reference (use daily) |
| Understand how to test it | testing-strategy.md | 30 min |
| See project status | PROGRESS.md | 5 min |
| Get quick answers | QUICK-REFERENCE.md | You are here |

---

## Module Quick Summary

| Module | Responsibility | Key File | Properties | Status |
|---|---|---|---|---|
| **Order Service** | Accept orders, track status | Task 1.1–1.8 | 9 | 🔵 Ready |
| **Inventory Service** | Reserve/release stock, ledger | Task 2.1–2.9 | 13 | 🔵 Ready |
| **Payment Service** | Simulate charge, configurable failures | Task 3.1–3.5 | 7 | 🔵 Ready |
| **Notification Service** | Send notifications, retry | Task 5.1–5.4 | 8 | 🔵 Ready |
| **Saga Orchestrator** 🔴 | Coordinate saga, **compensate** | Task 6.1–6.9 | 10 | 🔵 Ready |
| **Kafka Layer** | Pub/sub, idempotency, DLQ | Task 4.1–4.4 | 12 | 🔵 Ready |
| **API Gateway** | Route, rate limit, circuit break | Task 7.1–7.4 | 4 | 🔵 Ready |
| **Observability** | Trace, log, metrics | Task 8.1–8.5 | 8 | 🔵 Ready |

---

## Phase Timeline

```
Days 1–2:    Phase 0: Setup (solution, Docker, NuGet)
Days 3–5:    Phase 1: Order Service
Days 6–8:    Phase 2: Inventory Service
Days 9–10:   Phase 3: Payment Service
Days 11–12:  Phase 4: Kafka Layer
Days 13–14:  Phase 5: Notification Service
Days 15–18:  Phase 6: Saga Orchestrator 🔴 (CRITICAL)
Days 19–20:  Phase 7: API Gateway
Days 21–22:  Phase 8: Observability
Days 23–25:  Phase 9: Integration Testing 🔴 (PROOF POINT)
Days 26–27:  Phase 10: UI
Day 28:      Phase 11: Docs & Polish

✅ Proof Points (must pass):
  - Day 15–18: All Saga Orchestrator properties pass (especially compensation)
  - Day 23–25: PaymentFailure_Compensation_Test passes
  - Day 23–25: OrchestratorCrash_Recovery_Test passes
  - Day 28: Trace visible in Jaeger for one order spanning all services
```

---

## Critical Paths (Don't Miss These)

### 🔴 Compensation Logic (This is the project)
- **Why**: Proves you understand distributed transactions without 2PC
- **Where**: design.md § Module 5.2, tasks.md § Task 6.4
- **Test**: tasks.md § Task 9.2 (force payment failure, verify compensation)
- **Success**: Stock ledger shows both reserve AND release entries; stock restored

### 🔴 Orchestrator Crash Recovery
- **Why**: Proves saga state is persisted; system doesn't lose orders on crash
- **Where**: design.md § Module 5.3, tasks.md § Task 6.5
- **Test**: tasks.md § Task 9.3 (kill orchestrator mid-saga, restart, verify resume)
- **Success**: Saga loads from DB; completes from where it left off

### Idempotency (Inbox Pattern)
- **Why**: Kafka is at-least-once; without idempotency, duplicate messages cause double reservations
- **Where**: requirements.md § 2.6, design.md § Kafka Layer
- **Test**: Property 2.1.1, Property 6.2.1, integration test § Task 9.4
- **Success**: Process same message twice = same result as once

### Trace Propagation Across Kafka
- **Why**: HTTP has W3C traceparent built-in; Kafka doesn't; must be manual
- **Where**: design.md § Module 8, testing-strategy.md § Part 2
- **Test**: Task 8.2, open Jaeger UI after happy path test
- **Success**: One Correlation ID visible in Jaeger spanning all 5 services

---

## Getting Started in 10 Minutes

```bash
# 1. Download/clone project (1 min)
git clone <this-project>
cd Distributed-Order-Management-System

# 2. Run bootstrap (5 min)
bash bootstrap.sh

# 3. Start infrastructure (2 min)
docker-compose up --wait

# 4. Build solution (2 min)
dotnet build

# 5. Verify infrastructure (verify all containers healthy)
docker-compose ps
```

If all containers are healthy (✓), you're ready for Phase 0.

---

## Daily Checklist

### Morning
- [ ] Read today's task from tasks.md
- [ ] Read acceptance criteria
- [ ] Understand what test needs to pass

### Daytime
- [ ] Write test first (if not already written)
- [ ] Implement code
- [ ] Run `dotnet build` (should have 0 warnings)
- [ ] Run `dotnet test` (tests should pass)

### End of Day
- [ ] All tests pass? ✅ Move to next task
- [ ] Tests fail? 🔴 Debug using testing-strategy.md § Part 4
- [ ] Update PROGRESS.md with status
- [ ] Commit code

### Weekly
- [ ] Sunday: Review week's progress
- [ ] Monday: Plan next week's tasks
- [ ] Verify all properties passing for modules completed so far

---

## Common Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run property-based tests only
dotnet test --filter "Category=Property"

# Run critical tests only (MUST pass on Day 28)
dotnet test --filter "Category=Critical"

# Run tests for one module
dotnet test Orders.Service.Tests

# Run specific test
dotnet test --filter "Test=PaymentFailure_Compensation_Test"

# Run with verbose output
dotnet test --logger "console;verbosity=detailed"

# Clean build
dotnet clean && dotnet build

# Check infrastructure
docker-compose ps

# View logs
docker-compose logs -f kafka
docker-compose logs -f postgres-orders

# Stop all infrastructure
docker-compose down

# Access Jaeger UI
open http://localhost:16686
```

---

## Property-Based Testing Cheat Sheet

### Pattern 1: Idempotency
```csharp
[Property]
public void Operation_WithDuplicateMessageId_IsIdempotent(
    Command cmd, Guid messageId)
{
    var result1 = Process(cmd, messageId);
    var result2 = Process(cmd, messageId);
    Assert.Equal(result1, result2);
}
```

### Pattern 2: Round Trip
```csharp
[Property]
public void ReserveAndRelease_RoundTrip_RestoresOriginal(
    Product product, int quantity)
{
    var initial = product.Stock;
    Reserve(product, quantity);
    Release(product, quantity);
    Assert.Equal(product.Stock, initial);
}
```

### Pattern 3: Monotonicity
```csharp
[Property]
public void RetryCount_Increases_Monotonically(Notification notif)
{
    var before = notif.RetryCount;
    AttemptDelivery(notif);
    var after = notif.RetryCount;
    Assert.GreaterThanOrEqual(after, before);
}
```

### Pattern 4: Invariant
```csharp
[Property]
public void Stock_NeverNegative(Product product, List<int> reservations)
{
    foreach (var qty in reservations)
        Try(() => Reserve(product, qty));
    
    Assert.GreaterThanOrEqual(product.Stock, 0);
}
```

---

## Test File Organization

```
Orders.Service.Tests/
├─ OrderServicePropertyTests.cs      ← Property 1.1.1–1.3.2
├─ OrderServiceIntegrationTests.cs   ← With real Postgres
├─ OrderServiceUnitTests.cs          ← Handler logic

Saga.Orchestrator.Tests/
├─ SagaStateMachinePropertyTests.cs   ← Property 5.1.1–5.1.4
├─ SagaCompensationPropertyTests.cs   ← 🔴 Property 5.2.1–5.2.5 (CRITICAL)
├─ SagaTimeoutPropertyTests.cs        ← Property 5.3.1–5.3.3
└─ SagaIntegrationTests.cs

Integration.Tests/
├─ EndToEndSagaTests.cs
│  ├─ HappyPath_Test()
│  ├─ PaymentFailure_Compensation_Test() 🔴 CRITICAL
│  ├─ OrchestratorCrash_Recovery_Test() 🔴 CRITICAL
│  ├─ DuplicateMessage_Suppression_Test()
│  ├─ PoisonMessage_DLQ_Test()
│  └─ ConcurrentOrders_Test()
└─ ChaosTests.cs
```

---

## Kafka Topics Quick Reference

```
Topics Created:
├─ order.events ..................... OrderPlaced, OrderConfirmed, OrderFailed
├─ order.commands ................... ConfirmOrder, FailOrder
├─ inventory.commands ............... ReserveInventory, ReleaseInventory
├─ inventory.events ................. InventoryReserved, InventoryRejected, InventoryReleased
├─ payment.commands ................. ChargePayment, RefundPayment
├─ payment.events ................... PaymentCharged, PaymentFailed, PaymentRefunded
├─ notification.events .............. OrderPlaced, OrderConfirmed, OrderFailed
│
└─ *.dlq (Dead Letter Queues)
   ├─ order.events.dlq
   ├─ inventory.commands.dlq
   ├─ inventory.events.dlq
   ├─ payment.commands.dlq
   ├─ payment.events.dlq
   ├─ order.commands.dlq
   └─ notification.events.dlq

Partitioning: By OrderId (guarantees order per order)
```

---

## Database Schemas Quick Reference

```
Orders Service (orders database):
├─ Orders (OrderId, Status, CustomerId, CreatedAt, ...)
├─ OrderItems (ProductId, Quantity, UnitPrice, ...)
├─ OrderStatusTransitions (audit trail)
└─ InboxMessages (idempotency)

Inventory Service (inventory database):
├─ Products (ProductId, Stock, Name, ...)
├─ ReservationLedger (OrderId, ProductId, Quantity, Type=[Reserve|Release], MessageId, ...)
└─ InboxMessages

Payment Service (payment database):
├─ Payments (PaymentId, OrderId, Amount, Status, ...)
├─ Refunds (RefundId, PaymentId, ...)
└─ InboxMessages

Notification Service (notification database):
├─ Notifications (OrderId, EventType, Status, RetryCount, ...)
├─ NotificationHistory (audit trail)
└─ InboxMessages

Saga Orchestrator (saga database):
├─ SagaState (SagaId, OrderId, CurrentState, CompletedSteps, ...)
├─ SagaStateTransition (audit trail)
└─ (no InboxMessages; saga state itself is idempotent key)
```

---

## Troubleshooting Quick Reference

| Problem | Solution |
|---|---|
| Docker containers won't start | `docker-compose down -v` then `docker-compose up --wait` |
| Tests timeout | Increase timeout in Testcontainers configuration (see testing-strategy.md) |
| Property test fails with cryptic error | Read the shrunk input; it's the minimal failing case |
| Saga stuck in Pending | Check orchestrator logs; likely waiting for event that never arrived |
| Trace not visible in Jaeger | Verify Correlation ID propagation in logs; check W3C traceparent in Kafka headers |
| Stock count wrong | Check ReservationLedger; verify reserve + release balance |
| Duplicate message not suppressed | Verify Inbox pattern; check MessageId is unique constraint in DB |
| Payment not failing | Check payment failure rate is set > 0; verify configurable injector is wired |

---

## Proof Point Checklist (Day 28)

```
Run: dotnet test --filter "Category=Critical"

Expected:
✅ PaymentFailure_Compensation_Test PASSED
   └─ Check: InventoryReleased event published? Stock restored?

✅ OrchestratorCrash_Recovery_Test PASSED
   └─ Check: Saga loaded from DB? Completed from persisted state?

Then manually:
✅ Place order via UI
✅ Open Jaeger: http://localhost:16686
✅ Search for order Correlation ID
✅ Verify one trace spanning: Gateway → Order → Saga → Inventory → Payment → Notification

If all ✅, project is successful!
```

---

## Key Takeaways

1. **Compensation is the project**: If payment fails, inventory auto-releases. No manual DB fixes.

2. **Property-based tests beat unit tests**: Find edge cases automatically; don't just test happy path.

3. **Real infrastructure matters**: Testcontainers with real Kafka/Postgres catches problems unit tests miss.

4. **Idempotency is architectural**: Inbox pattern + MessageId at every async boundary.

5. **Trace context must cross Kafka**: HTTP built-in; Kafka manual. Don't skip this.

6. **Test-driven design works**: Write properties first; implementation follows tests.

7. **Eventual consistency is explicit**: System never lies about guarantees; lag is acceptable by design.

---

## Resources

| Resource | Location |
|---|---|
| Full Requirements | requirements.md |
| Technical Design | design.md |
| Day-by-Day Tasks | tasks.md |
| Testing Guide | testing-strategy.md |
| Project Status | PROGRESS.md |
| Quick Lookup | QUICK-REFERENCE.md (this file) |
| Bootstrap Script | bootstrap.sh |
| Docker Setup | docker-compose.yml |

---

## Final Thought

> **Success looks like**: Force payment to fail, watch compensation execute automatically, see inventory released, observe order marked failed, receive notification—all without touching a database. That's when you know you've built a real distributed system.

---

**Status**: ✅ Ready to start Phase 0  
**Time to Success**: 28 days  
**Effort Required**: High-focus, disciplined implementation following tasks.md  
**Payoff**: Deep understanding of saga orchestration, compensation, idempotency, and distributed tracing  

Good luck! 🚀

