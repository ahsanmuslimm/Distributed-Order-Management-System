/**
 * Product catalog types
 */
export interface Product {
  productId: string;
  name: string;
  description: string;
  price: number;
  createdAt: string;
}

export interface ProductCatalogResponse {
  products: Product[];
  cachedAt: string;
  isFromCache: boolean;
}

/**
 * Order types
 */
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

/**
 * UI State types
 */
export interface OrderFormData {
  customerId: string;
  items: Array<{
    productId: string;
    quantity: number;
    unitPrice: number;
  }>;
}

export interface CartItem {
  productId: string;
  product: Product;
  quantity: number;
}

export type OrderStatusType = 'Pending' | 'Confirmed' | 'Failed';
