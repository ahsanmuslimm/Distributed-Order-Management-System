import axios, { AxiosInstance } from 'axios';
import {
  Product,
  OrderRequest,
  OrderResponse,
  OrderStatus,
  ProductCatalogResponse
} from '../types/models';

/**
 * HTTP Client for distributed order management system
 * 
 * Base URL: http://localhost:5000 (API Gateway)
 * 
 * Available endpoints:
 * - GET /api/catalog - List all products
 * - POST /api/orders - Place new order
 * - GET /api/orders/{orderId} - Get order status
 */
class ApiService {
  private client: AxiosInstance;
  private baseURL: string;

  constructor(baseURL: string = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000') {
    this.baseURL = baseURL;
    this.client = axios.create({
      baseURL: this.baseURL,
      headers: {
        'Content-Type': 'application/json'
      }
    });
  }

  /**
   * Get all products from catalog (cached)
   * 
   * GET /api/catalog
   * Response: 200 OK
   * 
   * @returns Array of products
   * @throws Error if request fails
   */
  async getProducts(): Promise<Product[]> {
    try {
      const response = await this.client.get<ProductCatalogResponse>('/api/catalog');
      console.log(`[API] Got ${response.data.products.length} products from ${response.data.isFromCache ? 'cache' : 'database'}`);
      return response.data.products;
    } catch (error) {
      console.error('[API] Failed to get products:', error);
      throw new Error('Failed to fetch products. Is the API Gateway running?');
    }
  }

  /**
   * Place a new order
   * 
   * POST /api/orders
   * Response: 202 Accepted
   * 
   * @param request Order request with customer ID and items
   * @returns Order response with new order ID
   * @throws Error if request fails
   */
  async placeOrder(request: OrderRequest): Promise<{ 
    response: OrderResponse;
    traceId?: string;
  }> {
    try {
      const response = await this.client.post<OrderResponse>('/api/orders', request, {
        validateStatus: (status) => status === 202 // Accept 202 Accepted
      });

      const traceId = response.headers['x-correlation-id'] as string | undefined;
      
      console.log(`[API] Order placed: ${response.data.orderId}`, {
        status: response.data.status,
        total: response.data.totalAmount,
        traceId
      });

      return {
        response: response.data,
        traceId
      };
    } catch (error) {
      console.error('[API] Failed to place order:', error);
      throw new Error('Failed to place order. Please check the API Gateway.');
    }
  }

  /**
   * Get order status
   * 
   * GET /api/orders/{orderId}
   * Response: 200 OK
   * 
   * @param orderId Order ID to query
   * @returns Order status with current details
   * @throws Error if order not found or request fails
   */
  async getOrderStatus(orderId: string): Promise<OrderStatus> {
    try {
      const response = await this.client.get<OrderStatus>(`/api/orders/${orderId}`);
      console.log(`[API] Order status: ${response.data.orderId} = ${response.data.status}`);
      return response.data;
    } catch (error: any) {
      if (error.response?.status === 404) {
        throw new Error(`Order ${orderId} not found`);
      }
      console.error(`[API] Failed to get order status for ${orderId}:`, error);
      throw new Error('Failed to fetch order status. Please try again.');
    }
  }

  /**
   * Get trace URL for order (for debugging)
   * 
   * @param orderId Order ID
   * @param traceId Trace ID from order response header
   * @returns URL to Jaeger trace
   */
  getTraceUrl(orderId: string, traceId?: string): string {
    if (!traceId) {
      return `http://localhost:16686/search?service=Gateway&tags={"orderId":"${orderId}"}`;
    }
    return `http://localhost:16686/search?traceID=${traceId}`;
  }
}

// Export singleton instance
export const apiService = new ApiService();
