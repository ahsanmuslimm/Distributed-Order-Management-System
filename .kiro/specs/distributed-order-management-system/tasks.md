# Implementation Tasks - Distributed Order Management System

## Overview

This document breaks down the Design phase into concrete, daily implementation tasks. Each task has a clear acceptance criterion based on property-based test passage. Tasks are organized by phase and module.

---

## Phase 0: Foundations (Days 1–2)

### Task 0.1: Solution Structure Setup

**Objective**: Create ASP.NET Core solution with 8 projects (one per service/module)

**Acceptance Criteria**:
- [ ] Global solution file created (Distributed-Order-Management-System.sln)
- [ ] Projects created:
  - Orders.Service
  - Inventory.Service
  - Payment.Service
  - Notification.Service
  - Saga.Orchestrator
  - Gateway
  - Contracts (shared DTOs)
  - Observability (shared utilities)
- [ ] All projects target .NET 8+
- [ ] All projects compile without errors

**Tasks**:
1. `dotnet new globaljson --sdk-version 8.0.x`
2. `dotnet new sln -n Distributed-Order-Management-System`
3. Create 8 projects using `dotnet new web` (or classlib)
4. Add projects to solution
5. Verify `dotnet build` passes

**Deliverables**:
- Directory structure established
- All projects compiling

---

### Task 0.2: Docker Compose Setup

**Objective**: Define Docker Compose with all infrastructure (Kafka KRaft, Postgres ×5, Redis, Jaeger)

**Acceptance Criteria**:
- [ ] docker-compose.yml created
- [ ] Services defined:
  - Kafka (KRaft mode, single broker)
  - PostgreSQL ×5 (Order, Inventory, Payment, Notification, Saga)
  - Redis
  - Jaeger
- [ ] All services start without error: `docker-compose up`
- [ ] Ports exposed:
  - Kafka: 9092
  - PostgreSQL: 5432 (varies per service)
  - Redis: 6379
  - Jaeger UI: 16686
- [ ] Health checks defined for each service

**Tasks**:
1. Create docker-compose.yml with above services
2. Define environment variables for connection strings
3. Define volume mounts for data persistence
4. Test startup: `docker-compose up --wait`

**Deliverables**:
- docker-compose.yml ready for use
- All services pass health checks

---

### Task 0.3: Shared Contracts Library

**Objective**: Create shared DTOs for message contracts (events and commands)

**Acceptance Criteria**:
- [ ] Contracts.csproj created
- [ ] Message base classes defined:
  - IEvent (with OrderId, CorrelationId, MessageId)
  - ICommand (with OrderId, CorrelationId, MessageId)
- [ ] Events defined:
  - OrderPlacedEvent
  - InventoryReservedEvent
  - InventoryRejectedEvent
  - InventoryReleasedEvent
  - PaymentChargedEvent
  - PaymentFailedEvent
  - PaymentRefundedEvent
  - OrderConfirmedEvent
  - OrderFailedEvent
- [ ] Commands defined:
  - ReserveInventoryCommand
  - ReleaseInventoryCommand
  - ChargePaymentCommand
  - RefundPaymentCommand
  - ConfirmOrderCommand
  - FailOrderCommand
- [ ] All DTOs use record types for immutability
- [ ] All projects reference Contracts project

**Tasks**:
1. Create base interfaces (IEvent, ICommand)
2. Implement all event and command DTOs
3. Add to Contracts.csproj
4. Reference in all service projects

**Deliverables**:
- Contracts library with all message types
- All projects compile with reference

---

### Task 0.4: MassTransit + Kafka Transport Setup

**Objective**: Wire up MassTransit with Kafka transport (trivial ping event end-to-end)

**Acceptance Criteria**:
- [ ] MassTransit NuGet packages added to all service projects
- [ ] Confluent.Kafka package added
- [ ] Startup configuration in Program.cs for each service:
  - MassTransit configured
  - Kafka transport configured (broker: kafka:9092)
  - Message serialization (JSON)
- [ ] Trivial test: PingEvent published and consumed end-to-end
- [ ] Test passes: `dotnet test` (Kafka integration test)

**Tasks**:
1. Add MassTransit and Confluent.Kafka NuGet packages
2. Configure MassTransit in Program.cs
3. Create trivial PingEvent and consumer
4. Write integration test (Testcontainers Kafka)
5. Verify test passes

**Deliverables**:
- MassTransit configured on all services
- Trivial pub-sub working end-to-end
- Testcontainers integration test passing

---

## Phase 1: Order Service (Days 3–5)

### Task 1.1: Order Entity & DbContext

**Objective**: Design Order aggregate and EF Core DbContext

**Acceptance Criteria**:
- [ ] Order entity created:
  - Properties: OrderId, CustomerId, Status, Items, CreatedAt, LastUpdatedAt, SagaId, Version
  - Status enum: Pending, Reserved, Charged, Confirmed, Failed, Compensated
  - Items collection (OrderItem entities)
- [ ] OrderItem entity created:
  - Properties: OrderItemId, OrderId, ProductId, Quantity, UnitPrice
- [ ] OrderStatusTransition entity created (audit trail):
  - Properties: TransitionId, OrderId, FromStatus, ToStatus, Reason, Timestamp, CorrelationId
- [ ] InboxMessages entity created (idempotency store):
  - Properties: MessageId (PK), OrderId, MessageType, Payload, ProcessedAt, CorrelationId
- [ ] OrderDbContext configured:
  - DbSet<Order>
  - DbSet<OrderItem>
  - DbSet<OrderStatusTransition>
  - DbSet<InboxMessages>
  - Fluent API configuration (constraints, indexes)
- [ ] Initial migration created: `dotnet ef migrations add InitialMigration`
- [ ] Migration applies without error: `dotnet ef database update`

**Tasks**:
1. Define Order, OrderItem, OrderStatusTransition, InboxMessages entities
2. Create OrderDbContext (DbContext subclass)
3. Configure DbSets and Fluent API
4. Create migration
5. Test migration applies to test database

**Deliverables**:
- Order.Service/Data/OrderDbContext.cs
- Order.Service/Entities/ (Order.cs, OrderItem.cs, etc.)
- Migration file

---

### Task 1.2: Place Order HTTP Endpoint

**Objective**: Implement POST /api/orders endpoint

**Acceptance Criteria**:
- [ ] Endpoint accepts OrderRequest DTO:
  - CustomerId, Items (product ID + quantity list)
  - Correlation-ID header (optional, generated if missing)
- [ ] On success:
  - Order created with status "Pending"
  - Returns 202 Accepted with OrderId in response
  - Correlation-ID included in response headers
- [ ] On validation error:
  - Returns 400 Bad Request (e.g., invalid customerId, empty items)
- [ ] On database error:
  - Returns 500 Internal Server Error
- [ ] Integration test passes (Testcontainers Postgres)

**Tasks**:
1. Create OrderRequest DTO
2. Create PlaceOrderHandler (application logic)
3. Create PlaceOrderEndpoint (HTTP handler)
4. Implement correlation ID extraction/generation
5. Write integration test

**Deliverables**:
- Orders.Service/Endpoints/PlaceOrderEndpoint.cs
- Orders.Service/Handlers/PlaceOrderHandler.cs
- Integration test in Orders.Service.Tests

---

### Task 1.3: Query Order Status HTTP Endpoint

**Objective**: Implement GET /api/orders/{id} endpoint

**Acceptance Criteria**:
- [ ] Endpoint accepts OrderId in path
- [ ] On success:
  - Returns 200 OK with Order DTO (OrderId, CustomerId, Status, Items, timestamps)
- [ ] On not found:
  - Returns 404 Not Found
- [ ] Correlation-ID from request propagated to response
- [ ] Integration test passes

**Tasks**:
1. Create GetOrderStatusQuery DTO
2. Create GetOrderStatusHandler (application logic)
3. Create GetOrderStatusEndpoint (HTTP handler)
4. Write integration test

**Deliverables**:
- Orders.Service/Endpoints/GetOrderStatusEndpoint.cs
- Orders.Service/Handlers/GetOrderStatusHandler.cs
- Integration test

---

### Task 1.4: Inbox Pattern Implementation

**Objective**: Implement deduplication logic for message consumption

**Acceptance Criteria**:
- [ ] Inbox processor created:
  - Check if MessageId exists in InboxMessages table
  - If found: Skip processing, mark as processed
  - If not found: Process message, atomically insert into InboxMessages + apply effect
- [ ] Unit tests pass (verify idempotency with duplicate MessageIds)
- [ ] Integration test passes (Testcontainers Postgres)

**Tasks**:
1. Create IInboxProcessor interface
2. Implement InboxProcessor (EF Core transaction)
3. Write unit tests (duplicate message handling)
4. Write integration test

**Deliverables**:
- Orders.Service/Infrastructure/InboxProcessor.cs
- Unit + integration tests

---

### Task 1.5: Correlation ID Propagation Middleware

**Objective**: Extract/generate Correlation-ID from request headers, propagate in responses

**Acceptance Criteria**:
- [ ] Middleware created:
  - Extract Correlation-ID from request header
  - If missing: Generate new UUID
  - Store in AsyncLocal context
- [ ] Response middleware adds Correlation-ID header to all responses
- [ ] Integration test passes

**Tasks**:
1. Create CorrelationIdMiddleware
2. Register in Program.cs
3. Write integration test

**Deliverables**:
- Orders.Service/Middleware/CorrelationIdMiddleware.cs
- Integration test

---

### Task 1.6: Property-Based Tests (Order Service)

**Objective**: Write all property-based tests for Order Service (Properties 1.1.1 – 1.3.2)

**Acceptance Criteria**:
- [ ] Test class created: OrderServicePropertyTests.cs
- [ ] All properties implemented (FsCheck/QuickCheck):
  - Property 1.1.1: Idempotent Order Creation
  - Property 1.1.2: Order State Determinism
  - Property 1.1.3: Event-State Correspondence
  - Property 1.1.4: Message_ID Uniqueness
  - Property 1.2.1: Query Result Consistency
  - Property 1.2.2: Status Progression Validity
  - Property 1.2.3: Timestamp Monotonicity
  - Property 1.3.1: Correlation_ID Presence
  - Property 1.3.2: Correlation_ID Immutability in Event
- [ ] All tests pass: `dotnet test Orders.Service.Tests`

**Tasks**:
1. Create OrderServicePropertyTests.cs
2. Implement each property as a test
3. Run tests until all pass

**Deliverables**:
- Orders.Service.Tests/OrderServicePropertyTests.cs
- All 9 properties passing

---

### Task 1.7: Structured Logging (Serilog)

**Objective**: Configure Serilog for structured logging with Correlation ID

**Acceptance Criteria**:
- [ ] Serilog NuGet package added
- [ ] Serilog configured in Program.cs:
  - Console sink for dev
  - JSON output format
  - Structured logging enrichment (correlation ID, user, etc.)
- [ ] Logging statements added to handlers and endpoints
- [ ] Logs include correlation ID in all entries

**Tasks**:
1. Add Serilog NuGet packages
2. Configure in Program.cs
3. Add logging to PlaceOrderHandler and GetOrderStatusHandler
4. Write integration test to verify correlation ID in logs

**Deliverables**:
- Orders.Service/Program.cs (Serilog configuration)
- Logging statements added to handlers

---

### Task 1.8: Order Service Prototype v0.1 Release

**Objective**: Tag and document Order Service as ready for integration

**Acceptance Criteria**:
- [ ] All property-based tests passing
- [ ] All integration tests passing
- [ ] README created with usage examples
- [ ] No compiler warnings or errors
- [ ] Code review passed

**Tasks**:
1. Verify all tests pass
2. Create Orders.Service/README.md
3. Run code analysis (no issues)
4. Commit and tag

**Deliverables**:
- Order Service v0.1 ready for integration with Saga Orchestrator

---

## Phase 2: Inventory Service (Days 6–8)

### Task 2.1: Reservation Ledger Design

**Objective**: Design immutable ledger for inventory movements

**Acceptance Criteria**:
- [ ] ReservationLedger entity created:
  - Properties: LedgerId, OrderId, ProductId, Quantity, Type (Reserve|Release), MessageId (unique), Status, CreatedAt, CorrelationId
  - Constraint: MessageId is unique (prevents duplicate entries)
  - Constraint: Quantity > 0
- [ ] Product entity created:
  - Properties: ProductId, Name, Description, Price, InitialStock
- [ ] DbContext configured with ReservationLedger and Product
- [ ] Initial migration created and applied

**Tasks**:
1. Create Product and ReservationLedger entities
2. Create InventoryDbContext
3. Create migration
4. Test migration

**Deliverables**:
- Inventory.Service/Data/InventoryDbContext.cs
- Inventory.Service/Entities/ (Product.cs, ReservationLedger.cs)

---

### Task 2.2: Stock Calculation from Ledger

**Objective**: Implement function to calculate current stock from immutable ledger

**Acceptance Criteria**:
- [ ] StockCalculator class created:
  - Method: CalculateStock(productId) → int
  - Logic: InitialStock - (sum of Reserves) + (sum of Releases)
- [ ] Unit tests pass (verify calculation with various ledger entries)
- [ ] Integration test passes

**Tasks**:
1. Create StockCalculator class
2. Implement CalculateStock method
3. Write unit + integration tests

**Deliverables**:
- Inventory.Service/Domain/StockCalculator.cs
- Tests

---

### Task 2.3: Reserve Inventory Command Handler

**Objective**: Implement idempotent reservation logic

**Acceptance Criteria**:
- [ ] ReserveInventoryHandler created:
  - Accepts ReserveInventoryCommand (ProductId, OrderId, Quantity, MessageId)
  - Checks InboxMessages for MessageId
  - Acquires row-level lock on Product row
  - Verifies sufficient stock
  - If sufficient: Create ReservationLedger entry, publish InventoryReservedEvent
  - If insufficient: Publish InventoryRejectedEvent
  - Always: Insert into InboxMessages atomically
- [ ] MassTransit consumer configured
- [ ] Unit + integration tests pass

**Tasks**:
1. Create ReserveInventoryHandler
2. Implement Inbox pattern check
3. Implement row-level locking (FOR UPDATE)
4. Implement event publishing
5. Write tests

**Deliverables**:
- Inventory.Service/Handlers/ReserveInventoryHandler.cs
- Inventory.Service/Consumers/ReserveInventoryConsumer.cs

---

### Task 2.4: Release Inventory Command Handler (Compensation)

**Objective**: Implement idempotent release logic for compensation

**Acceptance Criteria**:
- [ ] ReleaseInventoryHandler created:
  - Accepts ReleaseInventoryCommand (ProductId, OrderId, Quantity, MessageId)
  - Checks InboxMessages for MessageId
  - Verifies reservation exists (OrderId, ProductId)
  - Creates ReleaseInventory ledger entry
  - Publishes InventoryReleasedEvent
  - Atomically updates inbox
- [ ] Unit + integration tests pass

**Tasks**:
1. Create ReleaseInventoryHandler
2. Implement validation and release logic
3. Write tests

**Deliverables**:
- Inventory.Service/Handlers/ReleaseInventoryHandler.cs
- Inventory.Service/Consumers/ReleaseInventoryConsumer.cs

---

### Task 2.5: Get Catalog Endpoint (with Redis Cache-Aside)

**Objective**: Implement GET /api/catalog endpoint with Redis cache

**Acceptance Criteria**:
- [ ] GetCatalogEndpoint created:
  - On request: Check Redis for "catalog" key
  - If hit: Return cached product list
  - If miss: Query Postgres, populate Redis with TTL (60s), return products
- [ ] Redis client configured (StackExchange.Redis)
- [ ] Fallback: If Redis down, query Postgres directly
- [ ] Integration test passes

**Tasks**:
1. Add StackExchange.Redis NuGet package
2. Configure Redis client in Program.cs
3. Create GetCatalogEndpoint
4. Implement cache-aside logic
5. Write integration test

**Deliverables**:
- Inventory.Service/Endpoints/GetCatalogEndpoint.cs
- Integration test

---

### Task 2.6: Get Stock Query Endpoint

**Objective**: Implement GET /api/products/{id}/stock endpoint (ledger-based, no cache for decisions)

**Acceptance Criteria**:
- [ ] GetStockEndpoint created:
  - Returns current stock calculated from ledger
  - Does not use Redis cache
- [ ] Integration test passes

**Tasks**:
1. Create GetStockEndpoint
2. Use StockCalculator
3. Write integration test

**Deliverables**:
- Inventory.Service/Endpoints/GetStockEndpoint.cs

---

### Task 2.7: Redis Cache Consistency Monitoring

**Objective**: Implement background job to detect and repair cache-ledger divergence

**Acceptance Criteria**:
- [ ] CacheConsistencyJob created (IHostedService):
  - Runs periodically (30s interval)
  - For each product: Compare cache stock vs. ledger stock
  - If divergence detected: Log warning, invalidate cache
- [ ] Integration test passes

**Tasks**:
1. Create CacheConsistencyJob
2. Register as IHostedService in Program.cs
3. Write integration test

**Deliverables**:
- Inventory.Service/Services/CacheConsistencyJob.cs

---

### Task 2.8: Property-Based Tests (Inventory Service)

**Objective**: Write all property-based tests for Inventory Service (Properties 2.1.1 – 2.3.4)

**Acceptance Criteria**:
- [ ] All 13 properties implemented (FsCheck + Testcontainers):
  - Property 2.1.1–2.1.5: Reservation properties
  - Property 2.2.1–2.2.4: Release properties
  - Property 2.3.1–2.3.4: Cache properties
- [ ] All tests pass: `dotnet test Inventory.Service.Tests`

**Tasks**:
1. Create InventoryPropertyTests.cs
2. Implement each property (using Testcontainers Postgres + Redis)
3. Run tests until all pass

**Deliverables**:
- Inventory.Service.Tests/InventoryPropertyTests.cs
- All 13 properties passing

---

### Task 2.9: Inventory Service Prototype v0.1 Release

**Acceptance Criteria**: Same as Task 1.8 (all tests pass, README, no warnings)

---

## Phase 3: Payment Service (Days 9–10)

### Task 3.1: Payment Entity & Configurable Failure Injector

**Objective**: Design Payment entity and failure injection mechanism

**Acceptance Criteria**:
- [ ] Payment entity created:
  - Properties: PaymentId, OrderId, CustomerId, Amount, Status, TransactionId, MessageId, CreatedAt, ChargedAt, Version
  - Status enum: Pending, Charged, Refunded, Failed
- [ ] Refund entity created:
  - Properties: RefundId, PaymentId, Amount, Status, MessageId, CreatedAt, ProcessedAt
- [ ] PaymentFailureInjector interface created
- [ ] ConfigurablePaymentFailureInjector implemented:
  - Constructor accepts FailureRate (0.0–1.0)
  - ShouldFail() method returns bool based on random probability
  - Configurable seed for reproducible tests

**Tasks**:
1. Create Payment and Refund entities
2. Create PaymentDbContext
3. Create IPaymentFailureInjector + ConfigurablePaymentFailureInjector
4. Create migration

**Deliverables**:
- Payment.Service/Entities/ (Payment.cs, Refund.cs)
- Payment.Service/Domain/PaymentFailureInjector.cs
- Payment.Service/Data/PaymentDbContext.cs

---

### Task 3.2: Charge Payment Command Handler

**Objective**: Implement charge with configurable failure injection

**Acceptance Criteria**:
- [ ] ChargePaymentHandler created:
  - Accepts ChargePaymentCommand (OrderId, Amount, MessageId, FailureRate)
  - Checks InboxMessages for MessageId
  - Calls PaymentFailureInjector.ShouldFail()
  - If failure: Publish PaymentFailedEvent
  - If success: Create Payment record, publish PaymentChargedEvent
  - Atomically update inbox
- [ ] MassTransit consumer configured
- [ ] Unit + integration tests pass

**Tasks**:
1. Create ChargePaymentHandler
2. Integrate PaymentFailureInjector
3. Create event publishing logic
4. Write tests

**Deliverables**:
- Payment.Service/Handlers/ChargePaymentHandler.cs
- Payment.Service/Consumers/ChargePaymentConsumer.cs

---

### Task 3.3: Refund Payment Command Handler

**Objective**: Implement idempotent refund (compensation)

**Acceptance Criteria**:
- [ ] RefundPaymentHandler created:
  - Accepts RefundPaymentCommand (PaymentId, Amount, MessageId)
  - Validates PaymentId exists and status is "Charged"
  - Creates Refund entry
  - Publishes PaymentRefundedEvent
  - Atomically update inbox
- [ ] Unit + integration tests pass

**Tasks**:
1. Create RefundPaymentHandler
2. Implement validation and refund logic
3. Write tests

**Deliverables**:
- Payment.Service/Handlers/RefundPaymentHandler.cs
- Payment.Service/Consumers/RefundPaymentConsumer.cs

---

### Task 3.4: Property-Based Tests (Payment Service)

**Objective**: Write all property-based tests for Payment Service (Properties 3.1.1 – 3.2.3)

**Acceptance Criteria**:
- [ ] All 7 properties implemented (FsCheck + Testcontainers)
- [ ] All tests pass

**Tasks**:
1. Create PaymentPropertyTests.cs
2. Implement each property
3. Run tests until all pass

**Deliverables**:
- Payment.Service.Tests/PaymentPropertyTests.cs
- All 7 properties passing

---

### Task 3.5: Payment Service Prototype v0.1 Release

---

## Phase 4: Kafka Layer (Days 11–12)

### Task 4.1: Kafka Producer Wrapper

**Objective**: Implement producer wrapper with MessageId injection and ACK wait

**Acceptance Criteria**:
- [ ] KafkaProducerWrapper created:
  - Method: PublishAsync(message: IMessage) → Result
  - Injects MessageId into message headers (or generates if missing)
  - Injects Correlation ID into headers
  - Waits for broker ACK
  - On timeout: Retry up to N times
  - On all retries fail: Return failure
- [ ] Unit tests pass
- [ ] Integration test passes (Testcontainers Kafka)

**Tasks**:
1. Create KafkaProducerWrapper class
2. Implement publish logic with ACK wait
3. Implement retry logic
4. Write tests

**Deliverables**:
- Observability/Kafka/KafkaProducerWrapper.cs
- Tests

---

### Task 4.2: Kafka Consumer Wrapper with Inbox Pattern

**Objective**: Implement consumer wrapper with deduplication via Inbox pattern

**Acceptance Criteria**:
- [ ] KafkaConsumerWrapper created:
  - Intercepts all message handling
  - Checks InboxMessages table for MessageId
  - If found: Skip processing, commit offset
  - If not found: Call handler, on success insert inbox record + commit offset
  - On handler exception: Retry up to N times, on max retries route to DLQ
- [ ] Unit + integration tests pass

**Tasks**:
1. Create KafkaConsumerWrapper class
2. Implement inbox check
3. Implement DLQ routing on max retries
4. Write tests

**Deliverables**:
- Observability/Kafka/KafkaConsumerWrapper.cs
- Tests

---

### Task 4.3: DLQ Topic Creation & Routing

**Objective**: Create DLQ topics and implement routing logic

**Acceptance Criteria**:
- [ ] DLQ topics created (one per source topic):
  - order.events.dlq
  - inventory.commands.dlq
  - inventory.events.dlq
  - payment.commands.dlq
  - payment.events.dlq
  - order.commands.dlq
  - notification.events.dlq
- [ ] DLQ routing logic implemented:
  - On max retries: Publish to {topic}.dlq with error context
  - Store original payload, error message, retry count
- [ ] Integration test passes

**Tasks**:
1. Create DLQ topic initialization code
2. Implement DLQ routing in consumer wrapper
3. Write integration test

**Deliverables**:
- Observability/Kafka/DLQRouter.cs
- Kafka topic initialization

---

### Task 4.4: Property-Based Tests (Kafka Layer)

**Objective**: Write all property-based tests for Kafka Layer (Properties 6.1.1 – 6.4.3)

**Acceptance Criteria**:
- [ ] All 12 properties implemented (Testcontainers Kafka)
- [ ] All tests pass

**Tasks**:
1. Create KafkaLayerPropertyTests.cs
2. Implement each property
3. Run tests until all pass

**Deliverables**:
- Kafka.Tests/KafkaLayerPropertyTests.cs
- All 12 properties passing

---

## Phase 5: Notification Service (Days 13–14)

### Task 5.1: Notification Entity & Retry Policy

**Objective**: Design Notification entity and retry backoff logic

**Acceptance Criteria**:
- [ ] Notification entity created:
  - Properties: NotificationId, OrderId, CustomerId, EventType, Message, Status, RetryCount, LastError, MessageId, CreatedAt, LastAttemptAt, DeliveredAt, CorrelationId
  - Status enum: Pending, Delivered, Failed, DLQ
- [ ] NotificationRetryPolicy created:
  - MaxRetries: 3
  - InitialBackoff: 1s
  - Multiplier: 2.0 (exponential)
  - MaxBackoff: 30s
  - Method: CalculateBackoff(attempt: int) → TimeSpan

**Tasks**:
1. Create Notification entity
2. Create NotificationDbContext
3. Create NotificationRetryPolicy
4. Create migration

**Deliverables**:
- Notification.Service/Entities/Notification.cs
- Notification.Service/Domain/NotificationRetryPolicy.cs

---

### Task 5.2: Event Consumers (OrderPlaced, OrderConfirmed, OrderFailed)

**Objective**: Implement event consumers for notifications

**Acceptance Criteria**:
- [ ] Consumers created:
  - OrderPlacedEventConsumer
  - OrderConfirmedEventConsumer
  - OrderFailedEventConsumer
  - (Each verifies inbox before processing)
- [ ] Each consumer:
  - Generates Notification record
  - Simulates delivery (logs message)
  - Marks as Delivered
  - Publishes notification (or logs it)
- [ ] Integration tests pass

**Tasks**:
1. Create consumers for each event type
2. Implement notification generation
3. Write integration tests

**Deliverables**:
- Notification.Service/Consumers/ (all event consumers)

---

### Task 5.3: Notification Retry Engine

**Objective**: Implement background job for retry logic

**Acceptance Criteria**:
- [ ] NotificationRetryJob created (IHostedService):
  - Runs periodically (5s interval)
  - Queries failed notifications
  - For each: Calculate backoff, if ready attempt delivery
  - On success: Mark Delivered
  - On max retries: Route to DLQ (update status to "DLQ")
- [ ] Integration test passes

**Tasks**:
1. Create NotificationRetryJob
2. Register as IHostedService
3. Write integration test

**Deliverables**:
- Notification.Service/Services/NotificationRetryJob.cs

---

### Task 5.4: Property-Based Tests (Notification Service)

**Objective**: Write all property-based tests (Properties 4.1.1 – 4.2.4)

**Acceptance Criteria**:
- [ ] All 8 properties implemented
- [ ] All tests pass

---

## Phase 6: Saga Orchestrator (Days 15–18) — CRITICAL

### Task 6.1: Saga State Machine Design

**Objective**: Define state machine states, events, transitions

**Acceptance Criteria**:
- [ ] States defined:
  - Start
  - ReservingInventory
  - ChargingPayment
  - Completed
  - Compensating
  - Failed
  - TimedOut
- [ ] Events defined:
  - OrderPlaced
  - InventoryReserved
  - InventoryRejected
  - PaymentCharged
  - PaymentFailed
  - InventoryReleased
- [ ] Transitions defined and documented

**Tasks**:
1. Document state machine diagram (Mermaid or visual)
2. List all states and events
3. Define transitions
4. Review for correctness

**Deliverables**:
- Saga.Orchestrator/Domain/OrderSagaState.cs (state definitions)
- Saga.Orchestrator/Domain/OrderSagaEvents.cs (event definitions)

---

### Task 6.2: MassTransit Saga State Machine Implementation

**Objective**: Implement state machine using MassTransit Automatonymous

**Acceptance Criteria**:
- [ ] OrderSagaDefinition created (extends SagaDefinition)
- [ ] OrderSaga state machine created:
  - All states defined
  - All events mapped to consumers
  - All transitions configured
  - Write-ahead persistence before command issuance
- [ ] SagaState persisted to Postgres
- [ ] Initial migration created

**Tasks**:
1. Create OrderSagaDefinition
2. Create OrderSaga state machine class
3. Configure all states and transitions
4. Create migration
5. Test initialization

**Deliverables**:
- Saga.Orchestrator/StateMachines/OrderSagaDefinition.cs
- Saga.Orchestrator/StateMachines/OrderSaga.cs
- Migration

---

### Task 6.3: Command Issuance (ReserveInventory, ChargePayment)

**Objective**: Implement command issuance on state transitions

**Acceptance Criteria**:
- [ ] On OrderPlaced event:
  - Initialize saga
  - Transition to "ReservingInventory"
  - Issue ReserveInventoryCommand to inventory.commands topic
- [ ] On InventoryReserved event:
  - Transition to "ChargingPayment"
  - Issue ChargePaymentCommand to payment.commands topic
- [ ] Commands include Correlation ID from event
- [ ] Integration test passes

**Tasks**:
1. Add command issuance logic to state machine
2. Configure topic endpoints
3. Write integration test

**Deliverables**:
- Command issuance logic in OrderSaga
- Integration test

---

### Task 6.4: Compensation Logic (Payment Failure → Release Inventory)

**Objective**: Implement compensation on payment failure — THIS IS THE CORE PROOF POINT

**Acceptance Criteria**:
- [ ] On PaymentFailed event:
  - Transition to "Compensating"
  - Issue ReleaseInventoryCommand (reverse of ReserveInventory)
- [ ] On InventoryReleased event:
  - Transition to "Failed"
  - Issue FailOrderCommand to order.commands topic
  - Log compensation completion
- [ ] **Integration test forces payment failure, verifies:**
  - InventoryReserved event published
  - PaymentFailed event published
  - ReleaseInventoryCommand issued
  - InventoryReleased event published
  - OrderFailed event published
  - Order marked "Failed"
  - Stock returns to original level
- [ ] **Integration test passes** (this is the critical gate)

**Tasks**:
1. Add compensation logic to state machine
2. **Force payment failure in test**
3. **Verify all compensation steps execute**
4. **Verify stock ledger balance**

**Deliverables**:
- Compensation logic in OrderSaga
- **CRITICAL integration test: PaymentFailure_Compensation_Test()**
- **Stock ledger audit showing balance restored**

---

### Task 6.5: Timeout Detection and Recovery

**Objective**: Implement timeout monitoring and saga resumption

**Acceptance Criteria**:
- [ ] SagaTimeoutMonitor job created:
  - Runs periodically (10s interval)
  - Queries sagas in non-terminal states
  - If age > 60s: Mark as "TimedOut", trigger compensation or escalate
- [ ] Integration test passes

**Tasks**:
1. Create SagaTimeoutMonitor (IHostedService)
2. Implement timeout detection logic
3. Write integration test

**Deliverables**:
- Saga.Orchestrator/Services/SagaTimeoutMonitor.cs

---

### Task 6.6: Admin Query Endpoint

**Objective**: Implement GET /api/admin/sagas/{id} for saga state inspection

**Acceptance Criteria**:
- [ ] Endpoint returns:
  - SagaId, OrderId, CurrentState, CompletedSteps, FailureReason
  - Full transition history
  - Correlation ID
- [ ] Integration test passes

**Tasks**:
1. Create GetSagaStateEndpoint
2. Create query handler
3. Write integration test

**Deliverables**:
- Saga.Orchestrator/Endpoints/GetSagaStateEndpoint.cs

---

### Task 6.7: Property-Based Tests (Saga Orchestrator)

**Objective**: Write all property-based tests (Properties 5.1.1 – 5.3.3), especially compensation

**Acceptance Criteria**:
- [ ] All 10 properties implemented
- [ ] **Property 5.2.1: Compensation Completeness — PASSING**
- [ ] **Property 5.2.2: Compensation Order Reversal — PASSING**
- [ ] **Property 5.2.3: Idempotent Compensation — PASSING**
- [ ] **Property 5.2.4: Failed Step Triggers Compensation — PASSING**
- [ ] **Property 5.2.5: Ledger Balance After Compensation — PASSING**
- [ ] All tests pass

**Tasks**:
1. Create SagaPropertyTests.cs
2. **Implement all compensation properties (5.2.x)**
3. Run tests until all pass
4. **Verify stock ledger balance**

**Deliverables**:
- Saga.Orchestrator.Tests/SagaStateMachinePropertyTests.cs
- Saga.Orchestrator.Tests/SagaCompensationPropertyTests.cs
- **All 10 properties passing, especially compensation**

---

### Task 6.8: Integration Test: Orchestrator Crash Recovery

**Objective**: Verify saga resumes correctly after orchestrator crash

**Acceptance Criteria**:
- [ ] Integration test:
  1. Start saga (OrderPlaced)
  2. Wait for "ReservingInventory" state persisted
  3. Kill orchestrator process
  4. Restart orchestrator
  5. Assert saga resumes and completes correctly
  6. Assert final state is "Completed" or "Failed"
- [ ] Test passes

**Tasks**:
1. Create OrchestratorCrashRecovery_Test()
2. Use Testcontainers for process control
3. Verify saga resumes from persisted state

**Deliverables**:
- Integration test in Saga.Orchestrator.Tests

---

### Task 6.9: Saga Orchestrator Prototype v0.1 Release

---

## Phase 7: API Gateway (Days 19–20)

### Task 7.1: YARP Configuration & Routing

**Objective**: Configure YARP reverse proxy with routes to backend services

**Acceptance Criteria**:
- [ ] appsettings.json configured:
  - Routes: /api/orders/* → Order Service
  - Routes: /api/catalog/* → Inventory Service
  - Routes: /api/admin/* → Saga Orchestrator
- [ ] Services configured with addresses
- [ ] Health checks configured

**Tasks**:
1. Add YARP NuGet package
2. Configure YARP in Program.cs
3. Define route table in appsettings.json
4. Test routing

**Deliverables**:
- Gateway/appsettings.json
- Gateway/Program.cs

---

### Task 7.2: Rate Limiter Implementation

**Objective**: Implement fixed-window rate limiter per API consumer

**Acceptance Criteria**:
- [ ] Rate limiter configured:
  - 100 requests/min per API key
  - Returns 429 Too Many Requests on limit exceeded
  - Includes X-RateLimit-Remaining header
- [ ] Property-based tests pass

**Tasks**:
1. Configure AspNetCore.RateLimiting
2. Implement policy
3. Write tests

**Deliverables**:
- Gateway rate limiter configuration

---

### Task 7.3: Circuit Breaker Implementation

**Objective**: Implement Polly circuit breaker on backend calls

**Acceptance Criteria**:
- [ ] Circuit breaker configured:
  - Opens after 5 consecutive 5xx errors within 30s
  - Half-open after 15s
  - On open: Returns 503 to client
- [ ] Property-based tests pass

**Tasks**:
1. Add Polly NuGet package
2. Configure circuit breaker policy
3. Write tests

**Deliverables**:
- Gateway circuit breaker configuration

---

### Task 7.4: Gateway Prototype v0.1 Release

---

## Phase 8: Observability (Days 21–22)

### Task 8.1: Correlation ID Propagation End-to-End

**Objective**: Ensure Correlation ID flows through all services

**Acceptance Criteria**:
- [ ] Correlation ID:
  - Generated at API Gateway
  - Propagated in HTTP headers (to all services)
  - Propagated in Kafka message headers (order.events, inventory.commands, etc.)
  - Included in all structured logs
- [ ] Property-based tests pass (Property 8.1.1 – 8.1.4)

**Tasks**:
1. Create CorrelationIdMiddleware (shared)
2. Register in all services
3. Inject into Kafka headers
4. Write integration test

**Deliverables**:
- Shared CorrelationIdMiddleware
- All services configured

---

### Task 8.2: W3C Trace Context Propagation Across Kafka

**Objective**: Propagate W3C traceparent in Kafka message headers

**Acceptance Criteria**:
- [ ] Trace context (W3C traceparent format):
  - Injected into Kafka message headers on produce
  - Extracted on consume
  - Linked in Jaeger (all services visible as single trace)
- [ ] Integration test verifies one Correlation ID spans all services

**Tasks**:
1. Implement traceparent injection/extraction
2. Configure Kafka producer/consumer
3. Write integration test

**Deliverables**:
- Trace context propagation logic
- Integration test

---

### Task 8.3: Structured Logging & Correlation ID

**Objective**: Ensure all logs include Correlation ID

**Acceptance Criteria**:
- [ ] Serilog configured:
  - JSON output
  - Enrichment: correlation ID, user, service name
  - All logs include correlation ID field
- [ ] Property-based tests pass (Property 8.2.1 – 8.2.4)

**Tasks**:
1. Configure Serilog in all services
2. Add enrichment
3. Write integration test

**Deliverables**:
- Serilog configuration

---

### Task 8.4: OpenTelemetry Instrumentation

**Objective**: Instrument all services with OTel SDK

**Acceptance Criteria**:
- [ ] OTel SDK configured:
  - ASP.NET Core auto-instrumentation (HTTP)
  - EF Core instrumentation (DB)
  - Kafka instrumentation (manual ActivitySource)
- [ ] Jaeger exporter configured
- [ ] All spans exported to Jaeger

**Tasks**:
1. Add OpenTelemetry NuGet packages
2. Configure OTel in all services
3. Implement Kafka manual instrumentation
4. Configure Jaeger exporter

**Deliverables**:
- OTel configuration in all services

---

### Task 8.5: Observability Prototype v0.1 Release

---

## Phase 9: Integration Testing (Days 23–25)

### Task 9.1: End-to-End Happy Path Test

**Objective**: Test complete order flow (happy path)

**Acceptance Criteria**:
- [ ] Test flow:
  1. Place order
  2. Verify InventoryReserved event
  3. Verify PaymentCharged event
  4. Verify OrderConfirmed event
  5. Verify notification sent
  6. Verify final order status is "Confirmed"
  7. Verify trace ID spans all services in Jaeger
- [ ] Test passes (Testcontainers stack)

**Tasks**:
1. Create EndToEndSagaTests.cs
2. Implement HappyPath_Test()
3. Run with Testcontainers

**Deliverables**:
- Integration.Tests/EndToEndSagaTests.cs

---

### Task 9.2: CRITICAL: Payment Failure Compensation Test

**Objective**: Verify compensation executes correctly on payment failure

**Acceptance Criteria**:
- [ ] Test flow:
  1. Place order
  2. InventoryReserved event published
  3. Force PaymentService to fail (configurable failure rate = 1.0)
  4. PaymentFailed event published
  5. **Verify ReleaseInventoryCommand issued** ← CRITICAL
  6. **Verify InventoryReleasedEvent published** ← CRITICAL
  7. **Verify OrderFailed event published** ← CRITICAL
  8. **Verify stock ledger shows balance (reserved – released = 0)** ← CRITICAL
  9. Verify final order status is "Failed"
- [ ] **Test passes** (this validates the entire compensation system)

**Tasks**:
1. Create PaymentFailure_Compensation_Test()
2. **Force payment failure**
3. **Verify all compensation steps execute**
4. **Verify stock ledger balance**

**Deliverables**:
- **CRITICAL test: PaymentFailure_Compensation_Test() passing**

---

### Task 9.3: CRITICAL: Orchestrator Crash Recovery Test

**Objective**: Verify saga resumes correctly after orchestrator crashes

**Acceptance Criteria**:
- [ ] Test flow:
  1. Start saga
  2. Persist state (wait for "ReservingInventory")
  3. Kill orchestrator process
  4. Restart orchestrator
  5. **Verify saga loads from persisted state** ← CRITICAL
  6. **Verify saga resumes and completes** ← CRITICAL
  7. Verify final state is "Completed" or "Failed"
- [ ] **Test passes**

**Tasks**:
1. Create OrchestratorCrash_Recovery_Test()
2. Implement process control (kill/restart)
3. Verify state resumption

**Deliverables**:
- **CRITICAL test: OrchestratorCrash_Recovery_Test() passing**

---

### Task 9.4: Duplicate Message Suppression Test

**Objective**: Verify at-least-once delivery doesn't cause double effects

**Acceptance Criteria**:
- [ ] Test flow:
  1. Manually publish same ReserveInventory message twice
  2. Verify stock decremented exactly once
  3. Verify only one ledger entry created
- [ ] Test passes

**Tasks**:
1. Create DuplicateMessage_Suppression_Test()
2. Manually republish messages
3. Verify idempotency

**Deliverables**:
- Integration test

---

### Task 9.5: Poison Message DLQ Test

**Objective**: Verify failed messages route to DLQ

**Acceptance Criteria**:
- [ ] Test flow:
  1. Inject handler that always throws
  2. Publish message
  3. Attempt consumption N times (exhaust retries)
  4. Verify message routes to DLQ topic
  5. Verify DLQ message includes error context
- [ ] Test passes

**Tasks**:
1. Create PoisonMessage_DLQ_Test()
2. Implement failing handler
3. Verify DLQ routing

**Deliverables**:
- Integration test

---

### Task 9.6: Concurrent Orders Test

**Objective**: Verify no partition blocking under concurrent load

**Acceptance Criteria**:
- [ ] Test flow:
  1. Submit 100 concurrent orders
  2. Each order goes through full saga
  3. Measure completion time
  4. Verify no order blocked indefinitely
  5. Verify all orders complete or fail with reason
- [ ] Test passes

**Tasks**:
1. Create ConcurrentOrders_Test()
2. Submit 100 orders concurrently
3. Verify completion

**Deliverables**:
- Integration test

---

### Task 9.7: Trace Propagation Verification in Jaeger

**Objective**: Verify one Correlation ID spans all services in Jaeger

**Acceptance Criteria**:
- [ ] After running happy path test:
  - Open Jaeger UI (http://localhost:16686)
  - Search for Correlation ID
  - Verify single trace shows all services: Order, Inventory, Payment, Saga, Notification
  - Verify trace hierarchy is correct
- [ ] Manual verification successful

**Tasks**:
1. Run happy path test
2. Navigate to Jaeger UI
3. Search and verify trace

**Deliverables**:
- Screenshot of complete trace in Jaeger

---

## Phase 10: UI Development (Days 26–27)

### Task 10.1: Checkout UI (React)

**Objective**: Build minimal checkout form

**Acceptance Criteria**:
- [ ] UI displays:
  - Product list (from Inventory Service)
  - "Add to cart" buttons
  - Cart summary
  - "Place Order" button
- [ ] On submit: POST /api/orders
- [ ] On success: Show order ID and "Processing" status

**Tasks**:
1. Create React component
2. Call /api/catalog endpoint
3. Call POST /api/orders
4. Display results

**Deliverables**:
- UI/src/pages/Checkout.tsx

---

### Task 10.2: Order Status Dashboard (React)

**Objective**: Build order tracking page

**Acceptance Criteria**:
- [ ] UI displays:
  - Order ID input or from checkout
  - Current status (Pending, Reserved, Charged, Confirmed, Failed)
  - Progress indicator (visual steps)
  - Poll for updates every 1.5s
- [ ] On completion: Show confirmation or failure reason

**Tasks**:
1. Create React component
2. Implement polling logic
3. Display status progress

**Deliverables**:
- UI/src/pages/OrderStatus.tsx

---

### Task 10.3: Admin Dashboard (React)

**Objective**: Build saga state inspection dashboard

**Acceptance Criteria**:
- [ ] UI displays:
  - Recent orders table (status, current step)
  - Color coding: amber (in-progress), green (completed), red (failed)
  - Click row to expand: full transition history + timestamps
  - Jaeger link button (deep link to trace)
  - DLQ inspector table (failed messages)
- [ ] Chaos panel (dev-only):
  - Button: "Force next payment to fail"
  - Button: "Kill Payment Service"

**Tasks**:
1. Create React component
2. Call GET /api/admin/sagas/{id}
3. Implement Jaeger link templating
4. Implement chaos controls (dev-only, feature-flagged)

**Deliverables**:
- UI/src/pages/AdminDashboard.tsx
- UI/src/components/ChaosPanel.tsx (dev-only)

---

## Phase 11: Documentation & Polish (Day 28)

### Task 11.1: Architecture Diagram

**Objective**: Create visual diagram of complete system

**Deliverables**:
- Mermaid diagram showing all services, Kafka topics, databases, Jaeger

---

### Task 11.2: "How to Demo" Guide

**Objective**: Create step-by-step guide for demonstrating failure scenarios

**Deliverables**:
- README section with commands to:
  - Force payment failure
  - Kill payment service mid-saga
  - Duplicate message replay
  - View saga state in admin dashboard
  - View trace in Jaeger

---

### Task 11.3: Property Coverage Report

**Objective**: Summarize all properties and their test status

**Deliverables**:
- Table showing all 50+ properties with test status (✅ passing)

---

### Task 11.4: Module README Files

**Objective**: Document each module's API and behavior

**Deliverables**:
- README.md for each service (Orders, Inventory, Payment, Notification, Saga, Gateway)

---

## Validation Gates (Throughout Implementation)

### Daily Gates

**Every Day**:
- [ ] `dotnet build` succeeds (no errors, no warnings)
- [ ] `dotnet test` passes (all unit + integration tests)
- [ ] No compiler diagnostics

### Phase Gates

**Phase 0 Complete**:
- [ ] Docker Compose spins up without error
- [ ] Trivial Kafka pub-sub works end-to-end

**Phase 1 Complete (Order Service)**:
- [ ] All 9 property-based tests passing
- [ ] Order can be placed and queried
- [ ] Correlation ID propagated in responses

**Phase 2 Complete (Inventory Service)**:
- [ ] All 13 property-based tests passing
- [ ] Inventory can be reserved and released
- [ ] Redis cache working
- [ ] Stock ledger calculating correctly

**Phase 3 Complete (Payment Service)**:
- [ ] All 7 property-based tests passing
- [ ] Payment charge/refund working
- [ ] Failure rate injectable and working

**Phase 4 Complete (Kafka Layer)**:
- [ ] All 12 property-based tests passing
- [ ] Idempotency suppressing duplicates
- [ ] DLQ routing failed messages

**Phase 5 Complete (Notification Service)**:
- [ ] All 8 property-based tests passing
- [ ] Notifications generated and retried
- [ ] Exponential backoff working

**Phase 6 Complete (Saga Orchestrator) — CRITICAL**:
- [ ] All 10 property-based tests passing
- [ ] **All 5 compensation properties passing**
- [ ] **Happy path test passing**
- [ ] **Payment failure compensation test passing** ← PROOF POINT
- [ ] **Orchestrator crash recovery test passing** ← PROOF POINT

**Phase 9 Complete (Integration Testing) — FINAL GATE**:
- [ ] Happy path test passing
- [ ] **Payment failure compensation test passing**
- [ ] **Orchestrator crash recovery test passing**
- [ ] Duplicate message suppression test passing
- [ ] Poison message DLQ test passing
- [ ] Concurrent orders test passing
- [ ] Trace visible in Jaeger for all services

---

## Definition of "Done" for the Entire Project

The project is complete and ready for review when:

1. ✅ All 50+ property-based tests passing
2. ✅ All 8 integration test scenarios passing (happy path + all failure modes)
3. ✅ Every failure mode from PRD §3 has a passing automated test
4. ✅ Trace propagation works end-to-end (single trace spans all services in Jaeger)
5. ✅ Compensation logic proven: force payment failure → inventory released → order failed
6. ✅ Orchestrator crash recovery proven: crash mid-saga → restart → resume correctly
7. ✅ No compiler warnings or errors
8. ✅ README with "How to Demo" section
9. ✅ No cross-service database queries
10. ✅ Structured logging with Correlation ID in all logs
11. ✅ Circuit breaker prevents cascading failures
12. ✅ Rate limiter enforces limits
13. ✅ DLQ stores failed messages with error context
14. ✅ Admin dashboard shows saga state and transitions
15. ✅ All repositories follow code review standards

**Then**: A reviewer can run `docker-compose up`, trigger each failure mode via admin dashboard or CLI commands, and observe the system self-heal correctly — proving mastery of distributed systems, saga orchestration, and idempotent message handling.

