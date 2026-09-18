# Bridge: Phase 8 → Phase 9 (Observability → Integration Testing)

**Date**: September 18, 2026  
**Status**: Transition document  
**Purpose**: Set up Phase 9 for maximum impact

---

## What Phase 8 Gave Us

Phase 8 delivered the **observability plumbing**:

| Component | What It Does | Enabled By |
|-----------|-------------|-----------|
| W3C Trace Context | Parse/create traceparent headers | W3CTraceContext.cs |
| Kafka Propagation | Inject/extract traceparent in messages | KafkaTraceContextPropagator.cs |
| OpenTelemetry | Auto-instrument services + export | OpenTelemetryConfiguration.cs |
| Jaeger | Visualize traces | Docker: localhost:16686 |
| Structured Logging | All logs include Correlation ID | Serilog (already exists) |

**Result**: Every request has a trace ID that flows through all services and is visible in Jaeger.

---

## What Phase 9 Will Prove

Phase 9 is the **critical proof point**. We will demonstrate that the system works end-to-end with observability:

### Test 1: Happy Path (2 hours)
```
Given: Order with 2 items, payment succeeds
When: Place order through API Gateway
Then: 
  ✅ Order status = Confirmed
  ✅ Inventory reserved
  ✅ Payment charged
  ✅ Single trace ID in Jaeger shows all steps
  ✅ No errors in spans
```

### Test 2: Payment Failure Compensation (4 hours - CRITICAL)
```
Given: Order with 2 items, payment will fail
When: Place order through API Gateway
Then:
  ✅ Order status = Failed
  ✅ Inventory was reserved (then released)
  ✅ Payment failed (no charge attempted after retry)
  ✅ Single trace ID shows:
     - OrderPlaced event
     - InventoryReserved event
     - PaymentFailed event
     - InventoryReleased event (compensation!)
     - OrderFailed event
  ✅ All in ONE trace, causality clear
```

**This is the value prop**: When payment fails, compensation is automatic. And we can SEE it happen in Jaeger.

### Test 3: Crash Recovery (3 hours)
```
Given: Saga in middle of processing
When: Crash Saga Orchestrator
And: Restart Saga Orchestrator
Then:
  ✅ Saga resumes from checkpoint
  ✅ Compensation still executes
  ✅ Trace shows recovery path
```

### Test 4: Concurrent Orders (2 hours)
```
Given: 5 simultaneous orders
When: All placed at same time
Then:
  ✅ Each order has different Trace ID
  ✅ No cross-contamination in logs
  ✅ Jaeger shows 5 separate traces
```

---

## How Phase 8 Enables These Tests

### Test 1: Happy Path

**Without Phase 8**:
- Run test
- Check final database state
- Hope nothing broke
- ❌ Can't see latencies, bottlenecks, or intermediate steps

**With Phase 8**:
- Run test
- Check Jaeger trace
- See exact sequence: 
  - gateway.http (15ms)
  - order.service (50ms)
  - inventory.service (35ms)
  - saga.orchestrator (42ms)
  - payment.service (28ms)
  - notification.service (22ms)
- ✅ Can identify bottlenecks, verify sequence

### Test 2: Payment Failure Compensation (MOST IMPORTANT)

**Without Phase 8**:
- Set up payment to fail
- Run test
- Check logs in 7 different files
- Try to piece together: "did compensation happen?"
- Maybe/maybe not clear
- ❌ Can't prove causality

**With Phase 8**:
- Set up payment to fail
- Run test
- Query Jaeger: "Show me the trace"
- See exact sequence:
  ```
  OrderPlaced 
    → InventoryReserved
      → PaymentFailed
        → InventoryReleased (!!!)
          → OrderFailed
  ```
- All linked by same Trace ID
- ✅ Crystal clear: "Compensation executed automatically"

### Test 3: Crash Recovery

**Without Phase 8**:
- Crash orchestrator mid-saga
- Restart
- Check if compensation happened
- Unsure if order ended up in correct state
- ❌ No visibility into recovery path

**With Phase 8**:
- Crash orchestrator mid-saga
- Restart
- Query Jaeger
- See:
  ```
  [First attempt] OrderPlaced → InventoryReserved → [CRASH]
  [Recovery] → InventoryReleased → OrderFailed
  
  Same Trace ID, clear recovery path
  ```
- ✅ Proves system handles crashes correctly

### Test 4: Concurrent Orders

**Without Phase 8**:
- 5 simultaneous orders
- Check logs
- Are all 5 visible?
- Did they interfere?
- Hard to tell
- ❌ Logs get interleaved, confusing

**With Phase 8**:
- 5 simultaneous orders
- Each has different Trace ID
- Jaeger shows 5 separate traces
- Zero interference
- ✅ Clear isolation

---

## Phase 9 Test Implementation Pattern

### Basic Structure

```csharp
// Phase 9 Integration Test

[Fact]
public async Task PaymentFailure_CompensationTriggered_InventoryReleased()
{
    // 1. Arrange
    var orderId = Guid.NewGuid();
    var productId = Guid.NewGuid();
    var quantity = 5;
    
    // Set up payment to fail
    _paymentService.SetupFailure();
    
    // 2. Act
    var response = await _httpClient.PostAsJsonAsync(
        "http://localhost:5000/api/orders",
        new { customerId = "cust-001", items = new[] { new { productId, quantity } } });
    
    // 3. Assert - Database State
    var order = await _orderDb.Orders.FindAsync(orderId);
    Assert.Equal(OrderStatus.Failed, order.Status);  // Order failed
    
    var inventory = await _inventoryDb.GetStockAsync(productId);
    Assert.Equal(1000, inventory.Stock);  // Stock restored (compensation!)
    
    // 4. Assert - Observability (NEW IN PHASE 9)
    var traceId = response.Headers.GetValues("traceparent").First();
    var trace = await JaegerQueryAsync(traceId);
    
    // Verify compensation chain in trace
    AssertSpanExists(trace, "OrderPlaced");
    AssertSpanExists(trace, "InventoryReserved");
    AssertSpanExists(trace, "PaymentFailed");
    AssertSpanExists(trace, "InventoryReleased");  // Compensation!
    AssertSpanExists(trace, "OrderFailed");
    
    // Verify causality (parent-child relationships)
    AssertSpanParent(trace, "InventoryReleased", "PaymentFailed");
    
    // ✅ Test PASSES: Compensation visible in trace!
}
```

---

## Pre-Phase-9 Checklist

Before starting Phase 9 integration tests, verify Phase 8 is operational:

### Observability Infrastructure

- [ ] Docker-compose has Jaeger service
- [ ] Jaeger UI accessible at http://localhost:16686
- [ ] OTLP gRPC collector listening on localhost:14250
- [ ] All services have OpenTelemetryConfiguration registered in Program.cs

### Trace Context Propagation

- [ ] HTTP requests include traceparent header (API Gateway → Order Service)
- [ ] Kafka messages include traceparent in headers
- [ ] All logs include CorrelationId field
- [ ] Jaeger can link spans by Trace ID

### Verification Commands

```bash
# 1. Check Jaeger is running
curl http://localhost:16686/api/services

# Expected output: 
# {"data":["Gateway","Order.Service","Inventory.Service",...]}

# 2. Make a request and check trace appears
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"cust-001","items":[{"productId":"prod-001","quantity":1}]}'

# 3. Query Jaeger UI
# Open http://localhost:16686
# Service: "Gateway"
# Click on latest trace
# Should see spans for all services linked by same Trace ID
```

---

## Phase 9 Test Strategy

### Test Categories

| Category | Count | Purpose | Observability |
|----------|-------|---------|----------------|
| Happy Path | 1 | Verify success case | Trace shows sequence |
| Failure Cases | 3 | Payment failure, timeout, exception | Trace shows compensation |
| Recovery | 2 | Crash/recovery, restart | Trace shows resume point |
| Concurrency | 2 | Multiple orders, race conditions | Multiple Trace IDs, isolation |
| **Total** | **8** | **Proof** | **All visible in Jaeger** |

### Test Sequence (Days 23-25)

**Day 23 (6 hours)**:
- Happy Path Test
- Basic failure test (payment fails once, retries succeed)
- Verify traces appear in Jaeger

**Day 24 (8 hours - CRITICAL)**:
- Payment Failure Compensation Test (force permanent failure)
- Verify compensation chain in trace
- Verify inventory released
- **This is the proof point**

**Day 25 (6 hours)**:
- Crash recovery test
- Concurrent orders test
- Clean up, document results

---

## How Phase 9 Proves the System

### Before Phase 9

"The system works" - based on:
- ✅ 157 unit/integration tests pass
- ✅ Property-based tests define correctness
- ✅ Code compiles without errors

### After Phase 9

"The system works and we can see it" - based on:
- ✅ All Phase 8 tests still pass
- ✅ **Integration tests verify end-to-end behavior**
- ✅ **Payment failure compensation visible in Jaeger trace**
- ✅ **Single Trace ID spans all services**
- ✅ **Latency breakdown per service**

---

## Phase 9 Success Definition

**Phase 9 is complete when**:

1. ✅ Happy path test passes
   - Order confirmed
   - Trace in Jaeger shows all spans
   
2. ✅ Payment failure compensation test passes (CRITICAL)
   - Order failed
   - Inventory released
   - Trace shows compensation chain
   - **Single Trace ID links all steps**
   
3. ✅ Recovery test passes
   - Saga resumes after crash
   - Compensation still executes
   
4. ✅ Concurrency test passes
   - 5 orders processed simultaneously
   - No cross-contamination
   - Each has separate Trace ID
   
5. ✅ Documentation complete
   - Test code documented
   - Jaeger screenshots in results

---

## Jaeger Query Patterns (for Phase 9 Tests)

### Find trace by Trace ID

```
GET http://localhost:16686/api/traces?service=Gateway&traceID=<uuid>
```

### Query spans by tag

```
GET http://localhost:16686/api/traces?service=Order.Service&tags={"error":"true"}
```

### Get all spans in trace

```
GET http://localhost:16686/api/traces/<traceId>
Response: {
  "data": [{
    "traceID": "<uuid>",
    "spans": [
      { "operationName": "http", "spanID": "...", "parentSpanID": "..." },
      { "operationName": "order.handler", "spanID": "...", "parentSpanID": "..." },
      ...
    ]
  }]
}
```

---

## Next: Phase 9 Kickoff

**Ready to Start Phase 9?**

Verify:
- [ ] Phase 8 code compiles
- [ ] Jaeger runs locally
- [ ] Services can be started
- [ ] Traces appear in Jaeger

**Then**:
1. Create EndToEndSagaTests.cs (Phase 9 Task 9.1)
2. Implement HappyPath_Test() (first green)
3. Implement PaymentFailure_CompensationTriggered() (proof point)
4. Implement CrashRecovery_Test() (resilience)
5. Implement ConcurrentOrders_Test() (isolation)

---

## Why This Matters

Phase 9 transforms this from:
- "We hope the saga works" (theory)
- **To**: "We watched the saga work" (proof)

And you can point to Jaeger and say:
- "Here's the order placed"
- "Here's the inventory reserved"
- "Here's the payment that failed"
- "Here's the inventory automatically released (compensation!)"
- "Here's the order marked failed"

**All in one trace. All causally linked. All observable.**

---

*Phase 8 → Phase 9: From Infrastructure to Proof*
