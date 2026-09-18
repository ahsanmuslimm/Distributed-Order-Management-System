# Phase 9 Quick Start Guide

**Days**: 23-25 (3 days)  
**Goal**: Integration tests that prove system works end-to-end  
**Critical Test**: Payment failure compensation visible in Jaeger trace

---

## Pre-Phase-9 Checklist

Before starting Phase 9, verify Phase 8:

- [ ] `dotnet build` succeeds (all projects)
- [ ] Docker-compose has Jaeger (check docker-compose.yml)
- [ ] All 157 existing tests compile
- [ ] Observability code has no syntax errors

---

## Phase 9 Overview

### 4 Integration Tests (8 test hours total)

| Test | Hours | Purpose | Critical? |
|------|-------|---------|-----------|
| Happy Path | 2 | Order successful, all steps, trace visible | No |
| Payment Failure | 4 | Payment fails, compensation auto-triggers | **YES** |
| Crash Recovery | 2 | Saga crashes, resumes, compensation completes | No |
| Concurrent Orders | 1 | 5 orders simultaneously, no contamination | No |

### Test Structure

Each test will verify:
1. **Database state** (existing approach)
2. **Jaeger trace** (NEW - Phase 8 enabled)

---

## Critical Test: Payment Failure Compensation

### Test Code Structure

```csharp
[Fact]
public async Task PaymentFailure_InventoryCompensated()
{
    // 1. ARRANGE: Set up payment to fail
    _paymentService.ConfigureFailure(ErrorType.Permanent);
    
    var productId = Guid.NewGuid();
    var initialStock = 1000;
    await _inventoryDb.SetStockAsync(productId, initialStock);
    
    // 2. ACT: Place order through gateway
    var response = await _httpClient.PostAsJsonAsync(
        "http://localhost:5000/api/orders",
        new {
            customerId = "cust-001",
            items = new[] { new { productId, quantity = 5 } }
        });
    
    var orderId = Guid.Parse(response.Content.ReadAsStringAsync().Result);
    var traceId = response.Headers.GetValues("traceparent").FirstOrDefault();
    
    // 3. ASSERT - Database State
    var order = await _orderDb.Orders.FindAsync(orderId);
    Assert.Equal(OrderStatus.Failed, order.Status);
    
    var stock = await _inventoryDb.GetStockAsync(productId);
    Assert.Equal(initialStock, stock);  // Restored!
    
    // 4. ASSERT - Trace (NEW!)
    var trace = await _jaegerClient.QueryTraceAsync(traceId);
    
    AssertSpanExists(trace, "OrderPlaced");
    AssertSpanExists(trace, "InventoryReserved");
    AssertSpanExists(trace, "PaymentFailed");
    AssertSpanExists(trace, "InventoryReleased");  // Compensation!
    AssertSpanExists(trace, "OrderFailed");
    
    // Verify causality
    AssertParentChild(trace, "InventoryReleased", "PaymentFailed");
    
    // ✅ Test passes: Compensation proven in trace!
}
```

### What To Verify in Trace

When you open Jaeger and search for this trace:

```
Trace ID: [from test response header]

Expected Spans (in order):
1. gateway.http
2. order.service.handler
3. kafka.producer (event to Kafka)
4. inventory.handler (received event, reserved)
5. kafka.producer (event to Kafka)
6. saga.orchestrator (received event)
7. kafka.producer (PAYMENT command to Kafka)
8. payment.service.handler (FAILS HERE)
9. kafka.producer (PaymentFailed event)
10. saga.orchestrator (received PaymentFailed)
11. kafka.producer (ReleaseInventory command - COMPENSATION!)
12. inventory.handler (received command, released)
13. kafka.producer (InventoryReleased event)

Key: Span 11 and 12 prove compensation happened!
```

---

## Jaeger Query Reference

### Query By Trace ID

```
Service: Gateway
Trace ID: (from test response header)
Find: Latest trace
Click: Expand all
Verify: All spans linked by same trace ID
```

### What To Look For

- **Red Spans**: Errors (payment failure should show here)
- **Span Hierarchy**: Parent-child relationships
- **Latencies**: How long each step took
- **Span Count**: Should be 8-12 spans for typical flow

---

## Test Execution Plan (Day by Day)

### Day 23 (6 hours): Happy Path + Basic Failure

**Hour 1-2**: Create EndToEndSagaTests.cs
```csharp
[Collection("Integration")]
public class EndToEndSagaTests : IAsyncLifetime
{
    private HttpClient _httpClient;
    private OrderDbContext _orderDb;
    private InventoryDbContext _inventoryDb;
    private JaegerClient _jaegerClient;
    
    public async Task InitializeAsync()
    {
        // Start testcontainers
        // Set up databases
        // Set up HTTP client
        // Wait for services
    }
    
    public async Task DisposeAsync()
    {
        // Clean up
    }
}
```

**Hour 3-4**: Happy Path Test
```csharp
[Fact]
public async Task HappyPath_OrderConfirmed_AllEventsProcessed()
{
    // Place order
    // Verify confirmed in DB
    // Query Jaeger
    // Verify trace visible
}
```

**Hour 5-6**: Basic Failure Test
```csharp
[Fact]
public async Task PaymentFails_OrderFailed_InventoryReleased()
{
    // Place order (payment fails)
    // Verify failed in DB
    // Verify inventory released
    // Query Jaeger
}
```

### Day 24 (8 hours): Payment Failure Compensation (CRITICAL)

**Hour 1-2**: Implement PaymentFailure_Compensation_Test
- Force permanent payment failure
- Verify compensation chain
- Query Jaeger trace
- Assert compensation happened

**Hour 3-4**: Debug & Fix
- Run test, capture failures
- Fix any issues
- Re-run until green

**Hour 5-6**: Trace Verification
- Open Jaeger (localhost:16686)
- Query by trace ID
- Manually verify compensation visible
- Screenshot for documentation

**Hour 7-8**: Documentation
- Document test approach
- Document Jaeger findings
- Note any edge cases

### Day 25 (6 hours): Recovery + Concurrency + Polish

**Hour 1-3**: Crash Recovery Test
```csharp
[Fact]
public async Task CrashDuringSaga_Recovery_CompensationResumes()
{
    // Start saga
    // Crash Saga Orchestrator at payment step
    // Restart Saga Orchestrator
    // Verify saga resumes
    // Verify compensation still executes
}
```

**Hour 4-5**: Concurrent Orders Test
```csharp
[Fact]
public async Task ConcurrentOrders_EachOrderIndependent()
{
    // Place 5 orders simultaneously
    // Each should have different trace ID
    // Verify no cross-contamination
    // Each completes successfully
}
```

**Hour 6**: Final Cleanup
- All tests green
- Documentation complete
- Ready for Phase 10

---

## Integration Test Patterns

### Setting Up Service Failure

```csharp
// In test setup
_paymentService.ConfigureFailure(new PaymentFailureConfig
{
    ErrorType = ErrorType.Permanent,
    FailureMode = FailureMode.AlwaysFail,
    RetryCount = 3  // Will retry 3 times before giving up
});
```

### Querying Jaeger

```csharp
// Get trace by ID
var trace = await _jaegerClient.GetTraceAsync(traceId);

// Assertions
Assert.NotNull(trace);
Assert.Equal(expectedSpanCount, trace.Spans.Count);

// Verify specific span
var orderPlacedSpan = trace.Spans.FirstOrDefault(s => s.OperationName == "OrderPlaced");
Assert.NotNull(orderPlacedSpan);
```

### Waiting for Async Processing

```csharp
// After placing order, wait for saga to complete
var completedAt = DateTime.UtcNow.AddSeconds(10);
while (DateTime.UtcNow < completedAt)
{
    var order = await _orderDb.Orders.FindAsync(orderId);
    if (order.Status != OrderStatus.Pending)
        break;
    
    await Task.Delay(100);
}

// Then query Jaeger
var trace = await _jaegerClient.QueryTraceAsync(traceId);
```

---

## Success Criteria for Phase 9

### Test 1: Happy Path
- [x] Order status = Confirmed
- [x] Inventory reserved
- [x] Payment charged
- [x] Notification sent
- [x] Trace in Jaeger shows all steps
- [x] No errors

### Test 2: Payment Failure (CRITICAL)
- [x] Order status = Failed
- [x] Inventory released (compensation!)
- [x] Payment not charged (or refunded)
- [x] Trace shows compensation chain
- [x] **Causality clear in Jaeger**
- [x] **InventoryReleased event AFTER PaymentFailed** (proves compensation)

### Test 3: Crash Recovery
- [x] Saga resumes after crash
- [x] Compensation still executes
- [x] Final state correct

### Test 4: Concurrency
- [x] 5 orders processed simultaneously
- [x] Each has separate trace ID
- [x] All reach final state
- [x] No data corruption

---

## Debugging Tips

### Trace Not Appearing in Jaeger

```
1. Check: curl http://localhost:16686/api/services
   Should list: ["Gateway", "Order.Service", ...]

2. Check: Are services exporting spans?
   Look for: ERROR logs about gRPC export failures

3. Check: Is JAEGER_HOST set correctly?
   Should be: localhost:14250 (in docker-compose, use "jaeger")

4. Fix: Restart services with debug logging
   Add: Log.Logger.MinimumLevel.Debug()
```

### Spans Missing

```
1. Check: Are all services instrumented?
   All services should call: services.AddOpenTelemetryTracing()

2. Check: Kafka headers being injected?
   Look for: message.InjectTraceContext() calls

3. Check: Consumer extracting trace context?
   Look for: headers.ExtractTraceContext() calls

4. Fix: Add manual instrumentation if needed
   Using: KafkaInstrumentationSource.RecordProducerOperation()
```

### Test Timeout

```
If test times out waiting for saga to complete:

1. Increase timeout in test
2. Check: Is Kafka running? docker ps
3. Check: Can services communicate? Logs should show activity
4. Check: Is payment failure injected correctly?
5. Reduce concurrency: Test with 1 order first, then scale
```

---

## Phase 9 Deliverables

### Code
- [ ] EndToEndSagaTests.cs (integration test file)
- [ ] JaegerClient.cs (helper to query Jaeger)
- [ ] 4 passing integration tests

### Documentation
- [ ] Test results (screenshots from Jaeger)
- [ ] Trace analysis (what we learned)
- [ ] Edge cases discovered
- [ ] Phase 9 completion summary

### Status
- [ ] All tests green (4/4)
- [ ] Compensation proven in traces
- [ ] Ready for Phase 10

---

## Quick Command Reference

```bash
# Start services
cd src/Orders.Service && dotnet run
cd src/Inventory.Service && dotnet run
cd src/Payment.Service && dotnet run
cd src/Saga.Orchestrator && dotnet run
cd src/Notification.Service && dotnet run
cd src/Gateway && dotnet run

# Run tests
cd tests/Orders.Service.Tests && dotnet test

# Query Jaeger
curl http://localhost:16686/api/services

# View Jaeger UI
# Open http://localhost:16686 in browser
```

---

## Next Phase (Phase 10-11)

After Phase 9 proves system works:

**Phase 10**: React UI (2 days)
- Checkout form
- Order status page
- Connect to API Gateway

**Phase 11**: Polish (1 day)
- Final docs
- Release v1.0

---

*Phase 9 is the proof point. Everything from Phases 0-8 comes together here to prove the system works end-to-end with full observability.*
