# Inventory Service

Part of the Distributed Order Management System (Phase 2).

## Overview

Manages inventory (stock) for the entire system. Core responsibilities:

1. **Reserve Inventory** - Lock stock when order is placed
2. **Release Inventory** - Unlock stock when order fails or is cancelled (compensation)
3. **Get Catalog** - List all products (cached in Redis)
4. **Get Stock** - Get current stock for a product (real-time from ledger)

## Architecture

### Immutable Ledger Pattern

Stock is never directly updated. Instead:

```
All inventory movements are append-only ledger entries:
  - Reserve: When order needs stock
  - Release: When compensating for failures

Current Stock = InitialStock - (sum of Reserves) + (sum of Releases)
```

**Why immutable?**
- Complete audit trail
- Deterministic: Stock calculation is reproducible
- Idempotent: Can replay same messages safely
- Reversible: Release entries can undo reserve entries

### Idempotency (Inbox Pattern)

Every reserve/release command has a unique `MessageId`. If the same `MessageId` is received twice:

```
First call:  Create ledger entry → Success
Second call: Check if MessageId exists → Return cached result (idempotent)
```

This ensures **at-least-once message delivery** is safe.

### Cache-Aside Pattern for Catalog

```
Request:
  1. Check Redis for "catalog" key
     ├─ HIT  → Return cached products (fast, ~1ms)
     └─ MISS → Query Postgres, cache with TTL (60s), return

Why?
  - Catalog changes rarely (products added maybe 1x/day)
  - But queried frequently (on every frontend page load)
  - Redis reduces Postgres load 100x
```

**Stock is NOT cached** - Always calculated from ledger for accuracy.

## API Endpoints

### 1. Reserve Inventory (Command)

**Not HTTP** - Comes from Kafka via MassTransit consumer.

```csharp
public record ReserveInventoryCommand
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public required Guid MessageId { get; init; }  // For idempotency
}
```

**Returns**: `InventoryReservedEvent` or `InventoryRejectedEvent`

**Logic**:
1. Check if `MessageId` already processed (idempotent)
2. Calculate current stock from ledger
3. If sufficient: Create `Reserve` ledger entry → Publish `InventoryReservedEvent`
4. If insufficient: Create `Rejected` ledger entry → Publish `InventoryRejectedEvent`

### 2. Release Inventory (Command - Compensation)

**Not HTTP** - Comes from Kafka via MassTransit consumer.

```csharp
public record ReleaseInventoryCommand
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public required Guid MessageId { get; init; }
}
```

**Returns**: `InventoryReleasedEvent`

**Logic**:
1. Check if `MessageId` already processed (idempotent)
2. Create `Release` ledger entry (always succeeds for audit)
3. Publish `InventoryReleasedEvent`

**Used when**:
- Payment fails → Release stock
- Order cancelled → Release stock
- Customer requests refund → Release stock

### 3. GET /api/catalog

```http
GET /api/catalog
X-Correlation-ID: 550e8400-e29b-41d4-a716-446655440000

Response:
{
  "products": [
    {
      "productId": "...",
      "name": "Widget",
      "description": "A useful widget",
      "price": 9.99,
      "createdAt": "2026-09-17T10:00:00Z"
    }
  ],
  "cachedAt": "2026-09-17T10:00:00Z",
  "isFromCache": true
}
```

**Caching**: Redis with 60s TTL
**Fallback**: Direct Postgres query if Redis is down

### 4. GET /api/products/{id}/stock

```http
GET /api/products/550e8400-e29b-41d4-a716-446655440000/stock
X-Correlation-ID: 550e8400-e29b-41d4-a716-446655440001

Response:
{
  "productId": "550e8400-e29b-41d4-a716-446655440000",
  "currentStock": 42,
  "calculatedAt": "2026-09-17T10:00:00Z"
}
```

**Calculation**: Real-time from ledger (no cache)
**Accuracy**: Always up-to-date (safe for order fulfillment decisions)

## Key Patterns

### 1. Structured Logging

All logs are JSON format with automatic `CorrelationId` enrichment:

```json
{
  "EventId": 0,
  "LogLevel": "Information",
  "Category": "Inventory.Service.Handlers.ReserveInventoryHandler",
  "Message": "Inventory reserved",
  "CorrelationId": "550e8400-e29b-41d4-a716-446655440000",
  "OrderId": "...",
  "ProductId": "...",
  "Quantity": 5
}
```

### 2. Correlation ID Propagation

Every HTTP request can include `X-Correlation-ID` header. If not provided, generated automatically. Flows through:

- HTTP request → AsyncLocal context → Message handlers → Database → Logs
- Allows end-to-end tracing across services

### 3. Optimistic Concurrency

Stock calculation uses consistent read (no locks on ledger) because:
- Ledger is immutable append-only
- Multiple concurrent reserves are safe
- If two reserves would exceed available, one gets `InventoryRejectedEvent`

## Database Schema

### Products Table

```sql
CREATE TABLE Products (
    ProductId UUID PRIMARY KEY,
    Name VARCHAR(255) NOT NULL UNIQUE,
    Description VARCHAR(1000) NOT NULL,
    Price NUMERIC(18,2) NOT NULL,
    InitialStock INTEGER NOT NULL,
    CreatedAt TIMESTAMP NOT NULL
);
```

### ReservationLedgers Table (Append-Only)

```sql
CREATE TABLE ReservationLedgers (
    LedgerId UUID PRIMARY KEY,
    ProductId UUID NOT NULL REFERENCES Products(ProductId) ON DELETE RESTRICT,
    OrderId UUID NOT NULL,
    Quantity INTEGER NOT NULL CHECK(Quantity > 0),
    Type INTEGER NOT NULL,  -- 0=Reserve, 1=Release
    MessageId UUID NOT NULL UNIQUE,  -- Prevents duplicates
    Status INTEGER NOT NULL,  -- 0=Pending, 1=Processed, 2=Rejected, 3=DeadLettered
    CorrelationId UUID NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    ProcessedAt TIMESTAMP NULL,
    
    -- Indexes for common queries
    INDEX(ProductId),
    INDEX(OrderId),
    INDEX(MessageId),
    INDEX(Status),
    INDEX(Type)
);
```

## Running Locally

### Prerequisites

- .NET 8 SDK
- PostgreSQL 15+
- Redis 7+
- Docker Compose (for infrastructure)

### Start Infrastructure

```bash
docker-compose up -d inventory_postgres inventory_redis
```

Wait for health checks to pass.

### Build

```bash
dotnet build Inventory.Service.csproj
```

### Apply Migrations

```bash
dotnet ef database update -p Inventory.Service.csproj
```

### Run

```bash
dotnet run --project Inventory.Service.csproj
```

Service starts on `http://localhost:5002`

### Endpoints

- API: http://localhost:5002/api/catalog
- Swagger: http://localhost:5002/swagger
- Health: http://localhost:5002/health

## Testing

### Run All Tests

```bash
dotnet test Inventory.Service.Tests.csproj
```

### Test Coverage

- **Stock Calculator**: 8 tests (calculation logic)
- **Reserve Handler**: 5 tests (idempotency, validation)
- **Release Handler**: 5 tests (compensation)
- **Get Catalog Endpoint**: 6 tests (cache-aside pattern)
- **Get Stock Endpoint**: 7 tests (real-time calculation)
- **Total**: 31 tests

### Key Test Scenarios

1. **Stock Calculation**
   - Reserve reduces stock
   - Release increases stock
   - Ignores Pending/Rejected entries
   - Never goes negative

2. **Idempotency**
   - Same MessageId returns cached result
   - Only one ledger entry created

3. **Cache-Aside**
   - Cache HIT returns fast
   - Cache MISS queries database
   - Cache invalidation works
   - Redis down → Fallback to Postgres

4. **Compensation**
   - Release always succeeds (audit trail)
   - Works without prior reservation (edge case)

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "Inventory": "Host=localhost;Port=5433;Database=inventory_service;Username=inventory;Password=inventory_password",
    "Redis": "localhost:6379"
  }
}
```

### Environment Variables

- `ASPNETCORE_ENVIRONMENT`: "Development" or "Production"
- `ConnectionStrings__Inventory`: Override Postgres connection
- `ConnectionStrings__Redis`: Override Redis connection

## Integration with Saga Orchestrator

### Message Flow

```
OrderPlaced
  │
  ├─→ [Saga Orchestrator]
  │     │
  │     └─→ ReserveInventory command
  │
  └─→ [Inventory Service]
        │
        ├─ If success: InventoryReservedEvent
        │   └─→ Next: [Payment Service]
        │
        └─ If failed: InventoryRejectedEvent
            └─→ [Saga Orchestrator marks order as FAILED]
```

### On Payment Failure

```
PaymentFailed
  │
  └─→ [Saga Orchestrator]
        │
        └─→ ReleaseInventory command
            │
            └─→ [Inventory Service]
                  │
                  └─→ InventoryReleasedEvent
                      (Stock restored automatically)
```

## Future Enhancements

- [ ] Redis cache consistency monitoring (Phase 2.7)
- [ ] Property-based tests (13 properties, Phase 2.8)
- [ ] Performance optimization (batch ledger reads)
- [ ] Analytics (stock movement reports)

## Troubleshooting

### Stock Calculation is Wrong

1. Check for Pending/Rejected ledger entries (should be ignored)
2. Verify all reserves/releases are marked `Processed`
3. Run `dotnet test` to verify calculation logic

### Cache is Stale

1. Check Redis connection: `redis-cli ping`
2. Invalidate manually: Call `InvalidateCatalogAsync()`
3. Check TTL: `redis-cli TTL catalog`

### Duplicate Reservations

1. Verify `MessageId` uniqueness constraint exists
2. Check logs for "already processed (idempotent)"
3. Should not happen - idempotency is guaranteed

## Related Documentation

- [Distributed Order Management System](../../README.md)
- [Design Document - Module 2](../../.kiro/specs/distributed-order-management-system/design.md)
- [Phase 2 Tasks](../../.kiro/specs/distributed-order-management-system/tasks.md)

