# Distributed Order Management System - Complete Specification

## Overview

This specification defines a **distributed order management system** built on ASP.NET Core, Kafka, PostgreSQL, and Redis. The system demonstrates correct implementation of:

- **Saga Pattern with Compensation**: Multi-step order workflows with automatic compensation on failure
- **Idempotent Message Consumption**: At-least-once Kafka delivery without duplicate effects
- **Eventual Consistency**: Distributed state across 5 services without distributed transactions
- **Distributed Tracing**: Complete order flows traceable end-to-end via Correlation ID
- **Resilience**: Circuit breakers, rate limiting, dead-letter queues, timeouts

**Core Value Proposition**: If a reviewer kills the Payment Service mid-saga, the system **automatically** releases inventory, marks the order as failed, and notifies the customer — without manual database surgery. This is not a theoretical exercise; it's proven by automated tests.

---

## Quick Start

### Prerequisites

- .NET 8 SDK
- Docker + Docker Compose
- (Optional) Visual Studio or Rider

### Run Locally

```bash
# Start all infrastructure (Kafka, Postgres ×5, Redis, Jaeger)
docker-compose up --wait

# Build all services
dotnet build

# Run all tests (property-based + integration)
dotnet test

# Start services (each in separate terminal, or use `dotnet run` in each service directory)
cd src/Orders.Service && dotnet run
cd src/Inventory.Service && dotnet run
cd src/Payment.Service && dotnet run
cd src/Notification.Service && dotnet run
cd src/Saga.Orchestrator && dotnet run
cd src/Gateway && dotnet run

# Access UI
open http://localhost:3000 (checkout)
open http://localhost:3000/admin (admin dashboard)

# Access Jaeger
open http://localhost:16686 (trace visualization)
```

---

## Specification Documents

### 1. Requirements Document (`.kiro/specs/distributed-order-management-system/requirements.md`)

**What**: Detailed requirements for all 8 modules (Order, Inventory, Payment, Notification, Saga, Kafka, Gateway, Observability)

**Contains**:
- 25+ requirements in EARS format (Ubiquitous, Event-driven, State-driven, Unwanted, Optional, Complex)
- 50+ property-based test specifications with mathematical notation
- Failure mode analysis for each module
- Cross-module dependencies and integration scenarios

**How to Read**: Start with Module 1 (Order Service) to understand the pattern, then follow module by module. Each requirement has:
- User story
- Acceptance criteria
- Property-based test specifications (with formal definitions)
- Failure modes (what breaks if not handled)

**Use This To**: Understand *what* the system must do and *how we verify* correctness.

---

### 2. Technical Design Document (`.kiro/specs/distributed-order-management-system/design.md`)

**What**: Architecture, component design, data models, and property-based test suites for each module

**Contains**:
- Architecture overview (diagram + component breakdown)
- Design constraints and component interactions
- Data models (EF Core DbContext definitions)
- Property-based test code examples (FsCheck/QuickCheck + C#)
- Prototype milestones and acceptance gates
- 11-phase implementation roadmap (28 days)

**How to Read**: For each module, read:
1. Design Constraints (what decisions were made and why)
2. Component Architecture (how it's structured)
3. Data Model (SQL schema)
4. Property-Based Test Suite (what tests validate correctness)
5. Prototype Milestone (definition of done)

**Use This To**: Understand *how* each module works and *what tests prove* it works correctly.

---

### 3. Implementation Tasks Document (`.kiro/specs/distributed-order-management-system/tasks.md`)

**What**: Day-by-day, task-by-task breakdown of what to build

**Contains**:
- 11 phases (Days 1–28)
- ~100+ concrete tasks (one task = 1 day or less)
- For each task:
  - Objective (what we're building)
  - Acceptance criteria (how we know it's done)
  - Step-by-step tasks
  - Deliverables (code, test files)

**Critical Tasks** (marked 🔴):
- Task 6.4: Compensation Logic (Payment Failure → Release Inventory) — **THE CORE PROOF POINT**
- Task 6.7: Property-Based Tests (Saga Orchestrator) — especially compensation properties
- Task 9.2: Payment Failure Compensation Test (integration) — **VALIDATES ENTIRE SYSTEM**
- Task 9.3: Orchestrator Crash Recovery Test — **VALIDATES RESILIENCE**

**How to Read**: Follow sequentially. Each task has clear acceptance criteria; when all criteria are met, move to the next task.

**Use This To**: Know *exactly what to build each day* and *when to move on*.

---

## Module Decomposition

### Module 1: Order Service
- **Purpose**: Accept orders, track status, store order records
- **Key Responsibility**: Never calls other services synchronously; publishes OrderPlaced event, consumes ConfirmOrder/FailOrder commands
- **Correctness**: Idempotent order creation (Property 1.1.1), status read consistency (Property 1.2.1)
- **Test Gate**: 9 properties passing, all integration tests passing
- **Prototype**: HTTP API + Kafka producer (async)

### Module 2: Inventory Service
- **Purpose**: Reserve stock, release stock (compensation), cache product catalog
- **Key Responsibility**: Immutable ledger of all stock movements, Redis cache-aside (not used for decisions)
- **Correctness**: Idempotent reservation (Property 2.1.1), round-trip reserve-release (Property 2.2.2), cache consistency (Property 2.3.1)
- **Test Gate**: 13 properties passing
- **Prototype**: Reservation ledger + release compensation + Redis cache

### Module 3: Payment Service
- **Purpose**: Simulate payment charges with configurable failure rate, refund when saga fails
- **Key Responsibility**: Configurable failure injection, naturally idempotent operations (create-if-not-exists)
- **Correctness**: Idempotent charge (Property 3.1.1), failure rate distribution (Property 3.1.2), refund validation (Property 3.2.3)
- **Test Gate**: 7 properties passing
- **Prototype**: Charge handler + refund handler + failure injector

### Module 4: Notification Service
- **Purpose**: Subscribe to order events, send notifications (simulated), retry on failure
- **Key Responsibility**: Pure event subscriber (no commands), exponential backoff, DLQ routing
- **Correctness**: Idempotent consumption (Property 4.1.1), retry monotonicity (Property 4.2.2)
- **Test Gate**: 8 properties passing
- **Prototype**: Event consumers + notification storage + retry job

### Module 5: Saga Orchestrator (🔴 CRITICAL)
- **Purpose**: Coordinate multi-step order workflow (reserve → charge → confirm), compensate on failure
- **Key Responsibility**: State machine, command issuance, **compensation logic** (this is the proof point)
- **Correctness**: State progression validity (Property 5.1.1), **compensation completeness (Property 5.2.1)**, **compensation order reversal (Property 5.2.2)**
- **Test Gate**: **All 10 properties passing, especially compensation**, **integration test forcing payment failure**
- **Prototype**: State machine + compensation + timeout handling

### Module 6: Kafka Layer
- **Purpose**: Reliable pub-sub with idempotency and dead-letter queues
- **Key Responsibility**: At-least-once delivery, message ID deduplication (Inbox pattern), DLQ routing
- **Correctness**: Delivery guarantee (Property 6.1.1), duplicate suppression (Property 6.2.1), DLQ routing (Property 6.3.1)
- **Test Gate**: 12 properties passing (Testcontainers Kafka)
- **Prototype**: Producer/consumer wrappers + DLQ router + topic initialization

### Module 7: API Gateway (YARP)
- **Purpose**: Single entry point, routing, rate limiting, circuit breaking
- **Key Responsibility**: Protect backend from overload, gracefully degrade on failures
- **Correctness**: Rate limit enforcement (Property 7.2.1), circuit breaker fault isolation
- **Test Gate**: All circuit breaker tests passing
- **Prototype**: YARP configuration + Polly policies + rate limiter

### Module 8: Observability
- **Purpose**: Distributed tracing, structured logging, metrics
- **Key Responsibility**: Correlation ID end-to-end, W3C trace context across Kafka, structured logs
- **Correctness**: Correlation ID immutability (Property 8.1.1), log trace grouping (Property 8.2.4)
- **Test Gate**: All trace propagation tests passing, complete trace visible in Jaeger
- **Prototype**: Middleware + trace context propagation + Serilog + OTel + Jaeger exporter

---

## Test Strategy

### Property-Based Tests

**Definition**: A property is a mathematical invariant that holds for all valid inputs.

**Example Property: Idempotent Reservation**
```
∀ reserveCmd ∈ ReserveInventoryCommands:
  ∀ messageId ∈ UniqueMessageIds:
    processReservation(reserveCmd, messageId) = processReservation(processReservation(reserveCmd, messageId), messageId)
```
*Translation*: Processing the same command twice with the same message ID produces the same result as processing it once.

**How We Test It**: FsCheck generates random `ReserveInventoryCommands` and `messageIds`, processes each twice with identical parameters, and asserts that stock count matches.

### Integration Tests (Testcontainers)

**Definition**: Real infrastructure (Kafka, PostgreSQL, Redis) running in containers; services communicate via actual message brokers and databases.

**Example: Payment Failure Compensation Test** (🔴 CRITICAL)
1. Place order via API
2. Verify InventoryReserved event
3. **Force payment to fail** (set failure rate = 1.0)
4. Verify PaymentFailed event
5. **Verify ReleaseInventoryCommand issued** (compensation)
6. Verify InventoryReleased event
7. Verify OrderFailed event
8. **Query inventory ledger: reserved - released = 0** (stock balance restored)

**Why This Matters**: This test proves the entire system works:
- Saga orchestration
- Event publishing
- Compensation logic
- Stock ledger consistency
- Eventual consistency

### Coverage Target

- **50+ Property-Based Tests**: Cover all correctness invariants (idempotency, consistency, monotonicity, etc.)
- **8 Integration Test Scenarios**:
  1. Happy path (order → reserve → charge → complete)
  2. 🔴 Payment failure → compensation (release → fail)
  3. 🔴 Orchestrator crash → recovery
  4. Duplicate message → suppression
  5. Poison message → DLQ
  6. Concurrent orders → no blocking
  7. Trace propagation → visible in Jaeger
  8. Circuit breaker → graceful degradation
- **Coverage Goal**: Every failure mode from PRD §3 has a passing automated test

---

## Critical Success Criteria

The project is successful if, on Day 28, you can:

### 1. Trigger Payment Failure and Verify Compensation

**Command**:
```bash
# Force payment to fail 100% of the time
export PAYMENT_FAILURE_RATE=1.0

# Restart Payment Service
docker-compose restart payment-service

# Place order via UI
curl -X POST http://localhost:8001/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId": "cust-123", "items": [{"productId": "prod-1", "quantity": 2}]}'

# Query saga state (admin API)
curl http://localhost:8005/api/admin/sagas/{orderId}
```

**Expected Result**:
- Order status: "Failed"
- Saga state transitions: OrderPlaced → ReservingInventory → ChargingPayment → Compensating → Failed
- Inventory ledger: One reserve entry + one release entry (balance = 0)
- Stock count: Returned to original level
- Notification: Sent with "Payment Failed" reason

### 2. Kill Orchestrator Mid-Saga and Verify Recovery

**Command**:
```bash
# Place order
curl -X POST http://localhost:8001/api/orders ...

# While saga is in-flight (e.g., ReservingInventory state)
# Kill orchestrator
docker-compose kill saga-orchestrator

# Observe: Order remains in Pending state (no progress)

# Restart orchestrator
docker-compose up saga-orchestrator

# Observe: Order progresses through saga (state machine resumes)
# Verify final state is Completed (or Failed if later step fails)
```

**Expected Result**:
- Saga resumes from persisted state
- No message loss
- No orphaned orders
- Final state correctly reached

### 3. View Complete Trace in Jaeger

**Command**:
```bash
# Place order
curl -X POST http://localhost:8001/api/orders ...

# Copy returned Correlation-ID header

# Open Jaeger UI
open http://localhost:16686

# Search for Correlation-ID

# Verify: Single trace with all services visible:
# - API Gateway → Order Service → Saga Orchestrator
# - Saga Orchestrator → Inventory Service
# - Saga Orchestrator → Payment Service
# - All services → Notification Service
# - Total latency: ~2-3 seconds (local)
```

**Expected Result**:
- One trace ID spans all service calls
- Kafka hops visible as spans (W3C traceparent propagation)
- Service dependencies visible in trace hierarchy
- Latencies measured end-to-end

### 4. Verify Idempotency Under Duplicate Delivery

**Command**:
```bash
# Place order (note order ID)

# Manually republish ReserveInventory message twice (via Kafka admin)
# (This simulates at-least-once delivery)

# Query inventory ledger
SELECT * FROM ReservationLedger WHERE order_id = '{orderId}'

# Query stock
SELECT current_stock FROM Products WHERE product_id = '{productId}'
```

**Expected Result**:
- One ledger entry (not two)
- Stock decremented once (not twice)
- Saga state unchanged (idempotent)

---

## File Structure

```
Distributed-Order-Management-System/
├─ .kiro/
│  └─ specs/
│     └─ distributed-order-management-system/
│        ├─ requirements.md (this specification)
│        ├─ design.md (technical design & test suites)
│        ├─ tasks.md (day-by-day implementation tasks)
│        └─ README.md (this file)
│
├─ src/
│  ├─ Orders.Service/
│  │  ├─ Orders.Service.csproj
│  │  ├─ Endpoints/ (HTTP)
│  │  ├─ Handlers/ (Application logic)
│  │  ├─ Entities/ (Domain)
│  │  ├─ Data/ (EF Core DbContext)
│  │  ├─ Consumers/ (Kafka)
│  │  └─ Program.cs
│  │
│  ├─ Inventory.Service/
│  │  ├─ (same structure)
│  │  ├─ Handlers/ReserveInventoryHandler.cs
│  │  ├─ Handlers/ReleaseInventoryHandler.cs
│  │  └─ Services/CacheConsistencyJob.cs
│  │
│  ├─ Payment.Service/
│  │  ├─ (same structure)
│  │  ├─ Domain/PaymentFailureInjector.cs
│  │  └─ Handlers/ChargePaymentHandler.cs
│  │
│  ├─ Notification.Service/
│  │  ├─ (same structure)
│  │  ├─ Consumers/ (event consumers)
│  │  └─ Services/NotificationRetryJob.cs
│  │
│  ├─ Saga.Orchestrator/
│  │  ├─ StateMachines/OrderSaga.cs
│  │  ├─ Endpoints/GetSagaStateEndpoint.cs
│  │  ├─ Services/SagaTimeoutMonitor.cs
│  │  └─ Data/ (saga state persistence)
│  │
│  ├─ Gateway/
│  │  ├─ Program.cs (YARP + Polly configuration)
│  │  └─ appsettings.json (routes, rate limits, circuit breaker)
│  │
│  ├─ Contracts/
│  │  ├─ Events/ (all event DTOs)
│  │  ├─ Commands/ (all command DTOs)
│  │  └─ Contracts.csproj
│  │
│  └─ Observability/
│     ├─ Middleware/CorrelationIdMiddleware.cs
│     ├─ Kafka/KafkaProducerWrapper.cs
│     ├─ Kafka/KafkaConsumerWrapper.cs
│     └─ Observability.csproj
│
├─ tests/
│  ├─ Orders.Service.Tests/
│  │  ├─ OrderServicePropertyTests.cs (Properties 1.1.1–1.3.2)
│  │  ├─ OrderServiceIntegrationTests.cs
│  │  └─ Orders.Service.Tests.csproj
│  │
│  ├─ (similar structure for other services)
│  │
│  └─ Integration.Tests/
│     ├─ EndToEndSagaTests.cs
│     │  ├─ HappyPath_Test()
│     │  ├─ PaymentFailure_Compensation_Test() 🔴
│     │  ├─ OrchestratorCrash_Recovery_Test() 🔴
│     │  ├─ DuplicateMessage_Suppression_Test()
│     │  ├─ PoisonMessage_DLQ_Test()
│     │  └─ ConcurrentOrders_Test()
│     │
│     └─ ChaosTests.cs
│        ├─ KillBroker_Test()
│        ├─ KillService_Test()
│        └─ NetworkPartition_Test()
│
├─ docker-compose.yml (all infrastructure)
├─ Distributed-Order-Management-System.sln
└─ README.md (this file)
```

---

## How to Read This Specification

### For Implementers (Building the System)

1. **Start Here**: Read this README for overview
2. **Read Requirements** (requirements.md): Understand what each module must do
3. **Read Design** (design.md): Understand how to build it and what tests to write
4. **Follow Tasks** (tasks.md): Do the implementation day-by-day
5. **Run Tests Daily**: Verify `dotnet test` passes before moving on

### For Reviewers (Evaluating the System)

1. **Quick Verification**: Run the integration tests
   ```bash
   dotnet test Integration.Tests --filter "Category=Critical"
   ```
   Should see:
   - ✅ HappyPath_Test
   - ✅ PaymentFailure_Compensation_Test
   - ✅ OrchestratorCrash_Recovery_Test

2. **Deep Dive**: Read design.md §5.2 (Compensation Logic) and task 6.4/9.2 (implementation)

3. **Live Demo**: Follow "Trigger Payment Failure and Verify Compensation" in Critical Success Criteria section

### For Learners (Understanding Distributed Systems)

1. **Start**: Design.md § Module 6 (Saga Orchestrator) — the heart of the system
2. **Focus**: Design.md § Task 6.4 (Compensation Logic) — the proof point
3. **Deep Dive**: Requirements.md §5.2 (Compensation Properties) — formal specification
4. **Verify**: Integration.Tests/EndToEndSagaTests.cs::PaymentFailure_Compensation_Test() — proof
5. **Understand**: How eventual consistency and compensating transactions solve distributed transaction problems

---

## Key Insights

### 1. Saga Orchestration is Explicit, Not Implicit

**Wrong Approach** (Choreography):
- Order Service publishes OrderPlaced
- Inventory Service receives it, reserves stock, publishes InventoryReserved
- Payment Service receives it, charges, publishes PaymentCharged
- (What state is this order in? Unclear — must reconstruct from event history)

**Correct Approach** (Orchestration, this project):
- Saga Orchestrator owns the state machine
- Orchestrator publishes commands (imperative) to services
- Services publish events (past tense) back to orchestrator
- (What state is this order in? Query saga state table directly)

**Benefit**: Single source of truth for saga state; compensation logic centralized and testable.

### 2. Compensation Must Be Bidirectional and Tested Early

**Wrong Approach**:
- Build happy path first
- "We'll handle compensation later"
- Result: Never actually tested; system silently leaves partial state

**Correct Approach** (this project):
- Design compensation logic upfront (design.md §5.2)
- Write property-based tests (tasks.md § 6.7)
- Force failure mid-saga in integration tests (tasks.md § 9.2)
- Verify inventory released and order marked failed (critical success criteria)

**Benefit**: Compensation isn't bolted on; it's core to the design.

### 3. Idempotency is Not Optional, It's Architectural

**Wrong Approach**:
- Assume Kafka is exactly-once (it's not)
- Write handlers without idempotency checks
- Result: Double reservations, double charges

**Correct Approach** (this project):
- Assume at-least-once delivery
- Every handler checks Inbox pattern (message ID already processed?)
- Every operation (reserve, charge, release) keyed by messageId
- Property: processTwice(cmd) ≡ processOnce(cmd)

**Benefit**: At-least-once becomes indistinguishable from exactly-once to the business logic.

### 4. Tracing is Not a Luxury, It's Load-Bearing

**Wrong Approach**:
- "We'll add logging later"
- Correlation ID optional
- Result: Untraceable bug reports ("Order stuck somewhere")

**Correct Approach** (this project):
- Correlation ID generated at entry point (API Gateway)
- Propagated in HTTP headers (to Order Service)
- Injected into Kafka message headers (W3C traceparent)
- Extracted by consumers and included in logs
- One Correlation ID → one complete trace in Jaeger spanning all services

**Benefit**: Any failure is traceable end-to-end; no guessing where things went wrong.

### 5. Tests Drive Design, Not Vice Versa

**Wrong Approach**:
- Design in vacuum
- Write code
- Scramble to write tests after (if at all)

**Correct Approach** (this project):
- Properties define correctness (requirements.md)
- Properties drive design decisions (design.md)
- Properties written before implementation (tasks.md)
- Implementation follows test definitions

**Benefit**: Implementation is correct by construction; tests are specifications, not afterthoughts.

---

## References

### Patterns & Principles

- **Saga Pattern**: Compensating transactions for distributed workflows (Newman, "Building Microservices")
- **Inbox Pattern**: Atomic persistence + publish for idempotent message consumption (Fowler, "Event Sourcing")
- **Property-Based Testing**: Generating random inputs to find edge cases (Hughes, QuickCheck; Claessen & Hughes)
- **Correlation IDs**: Tracing requests across service boundaries (Zipkin, Jaeger)
- **Circuit Breaker**: Fault isolation and graceful degradation (Fowler, "Release It!")

### Technologies

- **ASP.NET Core**: Web framework and dependency injection
- **EF Core**: ORM with LINQ query syntax
- **MassTransit**: Distributed application framework (abstracts Kafka complexity)
- **Kafka**: Distributed event streaming platform
- **Testcontainers**: Spin up infrastructure in containers for tests
- **OpenTelemetry**: Standardized observability (logging, tracing, metrics)
- **Jaeger**: Distributed tracing visualization
- **YARP**: ASP.NET Core reverse proxy
- **Polly**: Resilience policies (retries, circuit breakers, timeouts)
- **Serilog**: Structured logging

---

## Questions This Project Answers

### "How do I build a distributed system that's correct?"

**This project shows**:
- Design = formal specifications (properties)
- Implementation = code that satisfies properties
- Verification = automated tests that enforce properties

### "What happens when a service fails mid-transaction?"

**This project shows**:
- Compensation logic automatically executes
- Inventory released (Task 6.4)
- Order marked failed (Task 9.2)
- Customer notified (Notification Service)
- **No manual intervention required**

### "How do I guarantee idempotency with at-least-once delivery?"

**This project shows**:
- Every message carries unique MessageId
- Consumer checks InboxMessages table before processing
- On duplicate: Skip and ACK (idempotent)
- On first receipt: Process, insert inbox record, commit offset (atomic)

### "How do I trace requests across service boundaries?"

**This project shows**:
- Correlation ID generated at entry point
- Propagated in HTTP headers
- Injected into Kafka message headers (W3C traceparent)
- One trace spans all services in Jaeger
- Any failure is instantly traceable

### "How do I prevent cascading failures?"

**This project shows**:
- Circuit breaker prevents calls to failing services
- Returns 503 immediately (no hanging)
- Fallback response (e.g., cached data)
- Saga compensation on timeout
- Graceful degradation, not cascade

---

## Next Steps

1. **Read this document** (you are here ✓)
2. **Read requirements.md**: Understand the 25+ requirements and 50+ properties
3. **Read design.md**: See how each module is built and tested
4. **Follow tasks.md**: Implement day-by-day
5. **Run tests continuously**: `dotnet test` should pass every day
6. **On Day 28**: Verify critical success criteria (payment failure compensation, orchestrator recovery, trace propagation)

---

**Author Notes**: This specification is written from the perspective of *correctness by design*. Every requirement is testable; every property is provable; every failure mode is handled explicitly. The goal is not to build a "production system" in 28 days, but to demonstrate deep understanding of distributed systems mechanics: how partial failures happen, how they're detected, and how they're compensated.

**Most Important**: The compensation flow (Task 6.4 + Task 9.2) is the proof point. If you can force payment to fail and watch inventory automatically release without manual intervention, you've understood the problem. Everything else in the system supports that core capability.

Good luck, and welcome to the distributed systems club. 🎯

