# Phase 10: React UI Implementation Guide

**Days**: 26-27 (2 days)  
**Goal**: Build React frontend for order placement and status tracking  
**API Gateway Base URL**: http://localhost:5000  

---

## Overview

Phase 10 builds a React UI that integrates with the distributed order management system. The UI provides:

1. **Checkout Page**: Place new orders with product selection and quantity
2. **Order Status Page**: Real-time order tracking with status updates
3. **HTTP Client**: Typed API integration with gateway

---

## API Contract for Frontend

### Available Endpoints

| Method | Endpoint | Purpose | Status Code |
|--------|----------|---------|------------|
| GET | `/api/catalog` | List all products | 200 OK |
| POST | `/api/orders` | Place new order | 202 Accepted |
| GET | `/api/orders/{orderId}` | Get order status | 200 OK |

### GET /api/catalog (Product List)

**Response** (200 OK):
```json
{
  "products": [
    {
      "productId": "uuid",
      "name": "Product Name",
      "description": "Product Description",
      "price": 99.99,
      "createdAt": "2026-09-18T00:00:00Z"
    }
  ],
  "cachedAt": "2026-09-18T12:00:00Z",
  "isFromCache": true
}
```

### POST /api/orders (Place Order)

**Request**:
```json
{
  "customerId": "uuid",
  "items": [
    {
      "productId": "uuid",
      "quantity": 5,
      "unitPrice": 99.99
    }
  ]
}
```

**Response** (202 Accepted):
```json
{
  "orderId": "uuid",
  "customerId": "uuid",
  "status": "Pending",
  "totalAmount": 499.95,
  "createdAt": "2026-09-18T12:00:00Z",
  "itemCount": 1
}
```

**Headers**:
- `x-correlation-id`: UUID for tracing

### GET /api/orders/{orderId} (Order Status)

**Response** (200 OK):
```json
{
  "orderId": "uuid",
  "customerId": "uuid",
  "status": "Confirmed",
  "totalAmount": 499.95,
  "createdAt": "2026-09-18T12:00:00Z",
  "items": [
    {
      "orderItemId": "uuid",
      "productId": "uuid",
      "quantity": 5,
      "unitPrice": 99.99
    }
  ]
}
```

**Status Values**:
- `Pending` - Order created, waiting for inventory
- `Confirmed` - All steps completed successfully
- `Failed` - Order failed (payment or other issue)

---

## Project Structure

```
src/UI/
├── package.json
├── tsconfig.json
├── vite.config.ts (if using Vite)
├── public/
│   └── index.html
├── src/
│   ├── main.tsx
│   ├── App.tsx
│   ├── App.css
│   ├── pages/
│   │   ├── CheckoutPage.tsx
│   │   └── OrderStatusPage.tsx
│   ├── components/
│   │   ├── ProductList.tsx
│   │   ├── OrderForm.tsx
│   │   ├── OrderStatus.tsx
│   │   └── LoadingSpinner.tsx
│   ├── services/
│   │   └── api.ts (HTTP client)
│   ├── types/
│   │   └── models.ts (TypeScript interfaces)
│   └── styles/
│       └── index.css
└── .env (environment configuration)
```

---

## Setup Instructions

### 1. Create React Project

```bash
cd src/UI

# Using Create React App
npx create-react-app . --template typescript

# OR using Vite (faster)
npm create vite@latest . -- --template react-ts
```

### 2. Install Dependencies

```bash
cd src/UI
npm install

# Additional packages
npm install axios
npm install react-router-dom  # For navigation
npm install @types/react @types/react-dom --save-dev
```

### 3. Create .env File

```bash
VITE_API_BASE_URL=http://localhost:5000
VITE_POLL_INTERVAL=2000  # Status poll interval (ms)
```

---

## Core Components

### 1. API Service (`src/services/api.ts`)

```typescript
import axios from 'axios';
import { Product, OrderRequest, OrderResponse, OrderStatus } from '../types/models';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

const client = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json'
  }
});

export const apiService = {
  // Get all products
  async getProducts(): Promise<Product[]> {
    const response = await client.get('/api/catalog');
    return response.data.products;
  },

  // Place new order
  async placeOrder(request: OrderRequest): Promise<OrderResponse> {
    const response = await client.post('/api/orders', request, {
      validateStatus: (status) => status === 202 // Accept 202
    });
    return response.data;
  },

  // Get order status
  async getOrderStatus(orderId: string): Promise<OrderStatus> {
    const response = await client.get(`/api/orders/${orderId}`);
    return response.data;
  }
};
```

### 2. Type Definitions (`src/types/models.ts`)

```typescript
export interface Product {
  productId: string;
  name: string;
  description: string;
  price: number;
  createdAt: string;
}

export interface OrderItem {
  orderItemId: string;
  productId: string;
  quantity: number;
  unitPrice: number;
}

export interface OrderRequest {
  customerId: string;
  items: Array<{
    productId: string;
    quantity: number;
    unitPrice: number;
  }>;
}

export interface OrderResponse {
  orderId: string;
  customerId: string;
  status: string;
  totalAmount: number;
  createdAt: string;
  itemCount: number;
}

export interface OrderStatus {
  orderId: string;
  customerId: string;
  status: string;
  totalAmount: number;
  createdAt: string;
  items: OrderItem[];
}
```

### 3. Checkout Page (`src/pages/CheckoutPage.tsx`)

**Features**:
- Display product catalog
- Allow product selection and quantity input
- Calculate total price
- Place order via API
- Show confirmation with Order ID

**User Flow**:
1. User sees product list (cached, fast)
2. User selects products and quantities
3. User clicks "Place Order"
4. Show loading spinner
5. API call to POST /api/orders (202 Accepted)
6. Show confirmation with Order ID
7. Link to Order Status page

### 4. Order Status Page (`src/pages/OrderStatusPage.tsx`)

**Features**:
- Display order details (ID, customer, total, items)
- Show current status (Pending, Confirmed, Failed)
- Poll for updates every 2 seconds
- Display status history
- Link back to checkout

**Status Flow**:
- Initial: "Pending" (just created)
- After 5-10s: "Confirmed" (all steps succeeded) OR "Failed" (payment or other issue)
- Show real-time updates as saga processes

---

## Implementation Timeline

### Day 26 (8 hours)

**Hour 1-2**: Project Setup
- Create React project
- Install dependencies
- Configure environment

**Hour 3-4**: API Service & Types
- Implement HTTP client
- Define TypeScript interfaces
- Add error handling

**Hour 5-6**: Checkout Component
- Product list component
- Order form component
- API integration

**Hour 7-8**: Testing
- Manual API calls
- Verify endpoints work
- Debug CORS issues (if any)

### Day 27 (8 hours)

**Hour 1-2**: Order Status Component
- Status display
- Polling logic (every 2 seconds)
- Status history

**Hour 3-4**: UI Polish
- Styling (CSS/Tailwind)
- Responsive design
- Loading states

**Hour 5-6**: End-to-End Testing
- Place order through UI
- Watch status updates
- Verify compensation if payment fails

**Hour 7-8**: Documentation
- Write README
- Document API integration
- Note any issues

---

## Key Integration Points

### 1. Real-Time Status Updates

**Polling Strategy**:
```typescript
useEffect(() => {
  const interval = setInterval(async () => {
    const status = await apiService.getOrderStatus(orderId);
    setCurrentStatus(status);
    
    // Stop polling when terminal state reached
    if (status.status === 'Confirmed' || status.status === 'Failed') {
      clearInterval(interval);
    }
  }, 2000); // Poll every 2 seconds

  return () => clearInterval(interval);
}, [orderId]);
```

### 2. Error Handling

- Network errors → Show error message
- 404 Not Found → Order doesn't exist (invalid ID)
- 500 Internal Server Error → Show retry button
- CORS errors → Verify Gateway CORS config

### 3. Trace ID Integration (Optional)

When placing order, extract Trace ID from response header:
```typescript
const response = await apiService.placeOrder(request);
const traceId = response.headers['x-correlation-id'];
console.log(`Order trace: http://localhost:16686/search?traceID=${traceId}`);
```

---

## Success Criteria

### Day 26 Checklist
- [ ] React project created
- [ ] Dependencies installed
- [ ] API service working
- [ ] HTTP client calls endpoints
- [ ] Types defined
- [ ] Checkout page renders
- [ ] Products display correctly

### Day 27 Checklist
- [ ] Order status page works
- [ ] Polling updates status
- [ ] UI is responsive
- [ ] CSS styling applied
- [ ] End-to-end flow works
- [ ] Place order → See confirmation
- [ ] Status updates in real-time
- [ ] Documentation complete

### Overall Success Criteria
- [ ] UI runs on http://localhost:3000
- [ ] Gateway at http://localhost:5000
- [ ] All endpoints accessible
- [ ] Order placement works
- [ ] Status updates work
- [ ] No console errors
- [ ] Responsive design

---

## Common Issues & Solutions

### CORS Error
**Error**: "Access to XMLHttpRequest blocked by CORS policy"  
**Solution**: Gateway has CORS enabled, but verify:
```bash
curl -i http://localhost:5000/api/catalog
# Should have: Access-Control-Allow-Origin: *
```

### 404 on /api/catalog
**Error**: "GET /api/catalog 404"  
**Solution**: Verify services running:
```bash
curl http://localhost:5002/api/catalog  # Direct to Inventory Service
curl http://localhost:5000/api/catalog  # Through Gateway
```

### Order Status Always "Pending"
**Error**: Status never updates to "Confirmed"  
**Solution**: Check if services processing:
```bash
# Check Order Service logs
# Check Saga Orchestrator logs
# Verify Kafka running: docker ps | grep kafka
```

### API Returns 500 Error
**Error**: "Internal Server Error"  
**Solution**:
- Check service logs for exceptions
- Verify databases running: `docker ps`
- Verify all 5 PostgreSQL instances exist

### Payment Failure Test
**Setup**:
1. Place order
2. Trigger payment failure (if test mode available)
3. Watch compensation in UI
4. Order status should change to "Failed"

---

## Technologies

| Component | Technology | Why |
|-----------|-----------|-----|
| Framework | React 18 | Declarative UI |
| Language | TypeScript | Type safety |
| Build Tool | Vite or CRA | Fast development |
| HTTP Client | Axios | Simple, typed |
| Styling | CSS/Tailwind | Responsive |
| Routing | React Router | Multi-page |

---

## File References

### After Implementation
- `src/UI/package.json` - Dependencies
- `src/UI/src/main.tsx` - Entry point
- `src/UI/src/App.tsx` - Root component
- `src/UI/src/pages/CheckoutPage.tsx` - Checkout
- `src/UI/src/pages/OrderStatusPage.tsx` - Status tracking
- `src/UI/src/services/api.ts` - HTTP client
- `src/UI/src/types/models.ts` - TypeScript interfaces

---

## Running the UI

```bash
cd src/UI
npm install
npm run dev

# UI runs on http://localhost:3000
# API Gateway on http://localhost:5000
```

---

## What Phase 10 Proves

1. ✅ UI can communicate with distributed system
2. ✅ Order placement works end-to-end
3. ✅ Real-time status updates work
4. ✅ System is user-accessible

---

*Phase 10: React UI for distributed order management system*

