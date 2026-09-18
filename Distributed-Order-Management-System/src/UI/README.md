# Order Management System - React UI

**Part of**: Distributed Order Management System  
**Phase**: 10 (User Interface)  
**Status**: Ready to Run  

---

## Overview

React-based user interface for the distributed order management system. Provides:

- **Checkout Page**: Browse products and place orders
- **Order Status Page**: Real-time order tracking with automatic updates
- **HTTP Integration**: Communicates with API Gateway (localhost:5000)
- **Responsive Design**: Works on desktop and mobile

---

## Quick Start

### Prerequisites

- Node.js 16+
- npm 8+
- API Gateway running on http://localhost:5000
- Services running (Order, Inventory, Saga, Payment, Notification)

### Install & Run

```bash
cd src/UI

# Install dependencies
npm install

# Start development server (http://localhost:3000)
npm run dev

# Build for production
npm run build

# Preview production build
npm npm preview
```

---

## Project Structure

```
src/UI/
├── src/
│   ├── main.tsx                    # React entry point
│   ├── App.tsx                     # Root component with routing
│   ├── App.css                     # App styles
│   │
│   ├── pages/
│   │   ├── CheckoutPage.tsx        # Order placement UI
│   │   └── OrderStatusPage.tsx     # Order tracking UI
│   │
│   ├── components/
│   │   └── LoadingSpinner.tsx      # Loading indicator
│   │
│   ├── services/
│   │   └── api.ts                  # HTTP client for API Gateway
│   │
│   ├── types/
│   │   └── models.ts               # TypeScript interfaces
│   │
│   └── styles/
│       ├── index.css               # Global styles
│       ├── checkout.css            # Checkout page styles
│       └── order-status.css        # Order status page styles
│
├── public/
│   └── index.html                  # HTML template
│
├── package.json                    # Dependencies
├── tsconfig.json                   # TypeScript config
├── vite.config.ts                  # Vite configuration
├── .env                            # Environment variables
└── README.md                       # This file
```

---

## Features

### Checkout Page (`/`)

**What It Does**:
- Displays product catalog (cached, fast)
- Allows product selection and quantity input
- Shows real-time order total
- Places order via API (202 Accepted)
- Shows confirmation with Order ID and Trace ID

**User Flow**:
1. Browse products (retrieved from `/api/catalog`)
2. Add products to cart and set quantities
3. Click "Place Order"
4. Wait for confirmation
5. See confirmation page with Order ID
6. Click "View Order Status" to track

**Key Components**:
- `ProductCard`: Individual product display with quantity selector
- `Cart Summary`: Shows items, quantities, and total
- `Confirmation View`: Shows order ID and link to Jaeger trace

### Order Status Page (`/order/:orderId`)

**What It Does**:
- Displays order details (ID, customer, total, items)
- Shows current order status (Pending, Confirmed, or Failed)
- Polls for status updates every 2 seconds
- Shows status information based on current state
- Links to Jaeger trace for debugging

**Status Flow**:
1. **Pending**: Order received, processing in saga
2. **Confirmed**: All steps succeeded, order ready for shipment
3. **Failed**: Payment or other issue, order cancelled (inventory auto-released)

**Key Features**:
- Auto-polling stops when order reaches terminal state
- Real-time updates without page refresh
- Shows process steps for pending orders
- Indicates automatic compensation for failed orders
- Debug information with Jaeger link

### Components

#### CheckoutPage
- **File**: `src/pages/CheckoutPage.tsx`
- **Purpose**: Order placement interface
- **Key Methods**:
  - `loadProducts()`: Fetch catalog
  - `handleAddToCart()`: Manage cart items
  - `handlePlaceOrder()`: Submit order

#### OrderStatusPage
- **File**: `src/pages/OrderStatusPage.tsx`
- **Purpose**: Order tracking interface
- **Key Methods**:
  - `loadOrder()`: Fetch current order status
  - `getStatusColor()`: Status visualization
  - Auto-polling via `useEffect`

#### LoadingSpinner
- **File**: `src/components/LoadingSpinner.tsx`
- **Purpose**: Loading indicator with message
- **Props**: `message?: string`

### API Service

**File**: `src/services/api.ts`

**Methods**:
```typescript
// Get product catalog (cached)
apiService.getProducts(): Promise<Product[]>

// Place new order
apiService.placeOrder(request: OrderRequest): Promise<{ 
  response: OrderResponse; 
  traceId?: string 
}>

// Get order status
apiService.getOrderStatus(orderId: string): Promise<OrderStatus>

// Get Jaeger trace URL
apiService.getTraceUrl(orderId: string, traceId?: string): string
```

### Type Definitions

**File**: `src/types/models.ts`

```typescript
interface Product {
  productId: string
  name: string
  description: string
  price: number
  createdAt: string
}

interface OrderResponse {
  orderId: string
  customerId: string
  status: string
  totalAmount: number
  createdAt: string
  itemCount: number
}

interface OrderStatus {
  orderId: string
  customerId: string
  status: string
  totalAmount: number
  createdAt: string
  items: OrderItem[]
}
```

---

## API Endpoints Used

### GET /api/catalog
Fetches product catalog
- **Cache**: 60s (Redis)
- **Fallback**: PostgreSQL
- **Response**: `ProductCatalogResponse` with products array

### POST /api/orders
Places new order (async processing)
- **Status**: 202 Accepted (order queued for processing)
- **Headers**: `x-correlation-id` for trace linking
- **Response**: `OrderResponse` with new order ID

### GET /api/orders/{orderId}
Gets current order status
- **Status**: 200 OK
- **Response**: `OrderStatus` with full order details
- **Polling**: Every 2 seconds in OrderStatusPage

---

## Styling

### Global Theme (`src/styles/index.css`)
- Color scheme (primary, success, danger, etc.)
- Component base styles (buttons, badges, etc.)
- Loading spinner animation
- Responsive utilities

### Page Styles
- `checkout.css`: Product grid, cart sidebar, confirmation
- `order-status.css`: Status header, details grid, polling indicator
- `App.css`: Layout, header, footer

### Design Principles
- Clean, modern interface
- Mobile-responsive
- Clear status indicators (colors, emojis)
- Intuitive user flow

---

## Configuration

### Environment Variables (`.env`)

```bash
VITE_API_BASE_URL=http://localhost:5000   # API Gateway URL
VITE_POLL_INTERVAL=2000                   # Status polling interval (ms)
```

### Vite Configuration (`vite.config.ts`)

- **Port**: 3000
- **Dev Server Proxy**: `/api` routes to Gateway
- **Build Output**: `dist/` directory
- **Source Maps**: Enabled for debugging

---

## Development Workflow

### Running Locally

```bash
# Terminal 1: Start UI development server
cd src/UI
npm run dev
# UI runs on http://localhost:3000

# Terminal 2-8: Start services (in separate terminals)
cd src/Orders.Service && dotnet run
cd src/Inventory.Service && dotnet run
cd src/Payment.Service && dotnet run
cd src/Saga.Orchestrator && dotnet run
cd src/Notification.Service && dotnet run
cd src/Gateway && dotnet run

# Or if services are already running...
# Just run: npm run dev
```

### Testing Workflow

1. **Load Checkout Page**:
   - Open http://localhost:3000
   - Verify products load (should see product list)
   - If empty, check services running

2. **Place Order**:
   - Select products and quantities
   - Click "Place Order"
   - Should see confirmation with Order ID
   - Note the order ID

3. **Track Order**:
   - Click "View Order Status"
   - Should see "Pending" status initially
   - Status should update to "Confirmed" in 5-10 seconds
   - Watch polling count increase

4. **View Trace** (Optional):
   - Click "Open Jaeger Trace" link
   - Should see distributed trace in Jaeger UI
   - Verify all services present in trace

---

## Common Issues

### Products Not Loading

**Problem**: Empty product list  
**Solution**:
1. Verify Inventory Service running: `http://localhost:5002`
2. Check catalog endpoint: `curl http://localhost:5000/api/catalog`
3. Verify services responding: `curl http://localhost:5000/health`

### Order Placement Fails

**Problem**: "Failed to place order" error  
**Solution**:
1. Check Order Service running: `http://localhost:5001`
2. Check error message in browser console
3. Verify all 5 PostgreSQL databases running: `docker ps | grep postgres`
4. Check Gateway logs for routing issues

### Status Not Updating

**Problem**: Order stuck on "Pending"  
**Solution**:
1. Check Saga Orchestrator running: `http://localhost:5005`
2. Check Kafka running: `docker ps | grep kafka`
3. Wait 10+ seconds (processing takes time)
4. Manual refresh: Click "Refresh Status" button
5. Check service logs for exceptions

### CORS Error

**Problem**: "Access to XMLHttpRequest blocked by CORS"  
**Solution**:
1. Verify Gateway has CORS enabled
2. Check `/api` proxy in `vite.config.ts`
3. Try clearing browser cache
4. Verify Gateway running on port 5000

---

## Debugging

### Browser Console

- Log output from API calls
- Error messages from failed requests
- Component lifecycle logging

### Network Tab

- Check API requests to `/api/*`
- Verify response status codes (202 for POST, 200 for GET)
- Check response headers for `x-correlation-id`

### Jaeger Integration

- Click "Open Jaeger Trace" to visualize distributed flow
- See all services involved in order processing
- Trace causality and latencies
- Identify where time is spent

### React DevTools

- Install React DevTools browser extension
- Inspect component state and props
- Track component re-renders
- Profile performance

---

## Production Build

```bash
# Build optimized production bundle
npm run build

# Output in: dist/

# Preview production build locally
npm run preview

# Deploy dist/ to web server
# (Nginx, Apache, AWS S3, Vercel, etc.)
```

---

## Performance Notes

### Optimization Strategy

1. **Catalog Caching**: Cached server-side (60s TTL) via Redis
2. **Polling Interval**: 2 seconds strikes balance between responsiveness and load
3. **Component Rendering**: React.memo or useMemo if needed
4. **Lazy Loading**: Consider React.lazy for large UIs

### Metrics to Monitor

- Initial load time: Should be < 2s
- API response time: `/api/catalog` ~50ms (cached) or ~100ms (DB)
- Order placement: 202 response < 500ms
- Status polling: 1-2 requests/second, lightweight responses

---

## Architecture Diagram

```
Browser (http://localhost:3000)
    ↓
React App
    ├─ CheckoutPage
    │   ├─ Load catalog (/api/catalog)
    │   └─ Place order (POST /api/orders)
    │
    ├─ OrderStatusPage
    │   ├─ Initial load (GET /api/orders/{id})
    │   └─ Polling every 2s until terminal
    │
    └─ API Service
        └─ HTTP calls to Gateway (localhost:5000)
            ↓
        API Gateway (YARP reverse proxy)
            ├─ /api/orders → Order Service (5001)
            ├─ /api/inventory → Inventory Service (5002)
            ├─ /api/payments → Payment Service (5003)
            └─ /api/sagas → Saga Orchestrator (5005)
```

---

## Technologies

| Technology | Version | Why |
|-----------|---------|-----|
| React | 18.2 | Declarative UI |
| TypeScript | 5.2 | Type safety |
| Vite | 4.5 | Fast dev server |
| React Router | 6.16 | Client-side routing |
| Axios | 1.5 | HTTP client |
| CSS | Modern | No build complexity |

---

## Files Reference

| File | Purpose |
|------|---------|
| `src/main.tsx` | React entry point |
| `src/App.tsx` | Root component + routing |
| `src/pages/CheckoutPage.tsx` | Order placement |
| `src/pages/OrderStatusPage.tsx` | Order tracking |
| `src/services/api.ts` | HTTP client |
| `src/types/models.ts` | TypeScript interfaces |
| `vite.config.ts` | Build configuration |
| `.env` | Environment variables |

---

## Next Steps

### After Phase 10

1. **Testing**: Manual testing of full order flow
2. **Performance**: Optimize if needed
3. **Deployment**: Build and deploy to hosting
4. **Phase 11**: Documentation and release

### Enhancement Ideas

- Search/filter products
- Order history page
- Customer profile page
- Payment method selection
- Admin dashboard for orders
- Real-time notifications (WebSocket)

---

## Support

**Issues**:
- Check "Common Issues" section above
- Review browser console logs
- Check API Gateway logs: `docker logs <gateway-container>`
- Verify all services running: `docker ps` or `dotnet ps`

**Jaeger Debugging**:
- Open http://localhost:16686
- Service: "Gateway"
- Search for recent traces
- Click on trace to view full flow

---

*Phase 10: React UI for distributed order management system*

