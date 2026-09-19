import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiService } from '../services/api'
import { Product, CartItem } from '../types/models'
import LoadingSpinner from '../components/LoadingSpinner'
import '../styles/checkout.css'

/**
 * Checkout Page
 * 
 * Allows users to:
 * 1. Browse product catalog
 * 2. Select products and quantities
 * 3. Place order via API
 * 4. Receive confirmation with order ID
 * 5. Navigate to order status page
 */
export default function CheckoutPage() {
  const navigate = useNavigate()
  
  // State
  const [products, setProducts] = useState<Product[]>([])
  const [cart, setCart] = useState<Map<string, CartItem>>(new Map())
  const [customerId] = useState(() => {
    // Generate or load customer ID (normally from auth)
    const saved = localStorage.getItem('customerId')
    if (saved) return saved
    
    const generated = `customer-${Math.random().toString(36).substr(2, 9)}`
    localStorage.setItem('customerId', generated)
    return generated
  })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [placing, setPlacing] = useState(false)
  const [confirmation, setConfirmation] = useState<{ orderId: string; traceId?: string } | null>(null)

  // Load products on mount
  useEffect(() => {
    loadProducts()
  }, [])

  // Load product catalog
  const loadProducts = async () => {
    try {
      setLoading(true)
      setError(null)
      const products = await apiService.getProducts()
      setProducts(products)
    } catch (err: any) {
      setError(err.message || 'Failed to load products')
      console.error('Error loading products:', err)
    } finally {
      setLoading(false)
    }
  }

  // Add/update item in cart
  const handleAddToCart = (product: Product, quantity: number) => {
    if (quantity <= 0) {
      cart.delete(product.productId)
    } else {
      const item: CartItem = {
        productId: product.productId,
        product,
        quantity
      }
      cart.set(product.productId, item)
    }
    setCart(new Map(cart))
  }

  // Place order
  const handlePlaceOrder = async () => {
    if (cart.size === 0) {
      setError('Please add items to your order')
      return
    }

    try {
      setPlacing(true)
      setError(null)

      // Build request
      const items = Array.from(cart.values()).map(item => ({
        productId: item.productId,
        quantity: item.quantity,
        unitPrice: item.product.price
      }))

      // Place order
      const result = await apiService.placeOrder({
        customerId,
        items
      })

      // Show confirmation
      setConfirmation({
        orderId: result.response.orderId,
        traceId: result.traceId
      })

      // Clear cart
      setCart(new Map())
    } catch (err: any) {
      setError(err.message || 'Failed to place order')
      console.error('Error placing order:', err)
    } finally {
      setPlacing(false)
    }
  }

  // Navigate to order status
  const handleViewOrder = () => {
    if (confirmation) {
      navigate(`/order/${confirmation.orderId}`)
    }
  }

  // Calculate total
  const total = Array.from(cart.values()).reduce(
    (sum, item) => sum + (item.product.price * item.quantity),
    0
  )

  // Loading state
  if (loading) {
    return <LoadingSpinner message="Loading products..." />
  }

  // Error state
  if (error && products.length === 0) {
    return (
      <div className="checkout-container">
        <div className="error-box">
          <h2>Error Loading Products</h2>
          <p>{error}</p>
          <button onClick={loadProducts} className="btn btn-primary">
            Retry
          </button>
        </div>
      </div>
    )
  }

  // Confirmation state
  if (confirmation) {
    return (
      <div className="checkout-container">
        <div className="confirmation-box">
          <h2>✅ Order Placed Successfully!</h2>
          <p className="confirmation-text">Your order has been created and is being processed.</p>
          
          <div className="confirmation-details">
            <div className="detail-row">
              <label>Order ID:</label>
              <code className="order-id">{confirmation.orderId}</code>
            </div>
            <div className="detail-row">
              <label>Customer ID:</label>
              <code>{customerId}</code>
            </div>
            <div className="detail-row">
              <label>Total:</label>
              <strong>${total.toFixed(2)}</strong>
            </div>
          </div>

          {confirmation.traceId && (
            <div className="trace-info">
              <p>📊 View trace in Jaeger:</p>
              <a 
                href={apiService.getTraceUrl(confirmation.orderId, confirmation.traceId)}
                target="_blank"
                rel="noopener noreferrer"
                className="trace-link"
              >
                Open Jaeger Trace
              </a>
            </div>
          )}

          <div className="confirmation-actions">
            <button onClick={handleViewOrder} className="btn btn-primary btn-large">
              View Order Status
            </button>
            <button 
              onClick={() => {
                setConfirmation(null)
                setCart(new Map())
              }}
              className="btn btn-secondary btn-large"
            >
              Place Another Order
            </button>
          </div>
        </div>
      </div>
    )
  }

  // Main checkout view
  return (
    <div className="checkout-container">
      <div className="checkout-content">
        <section className="products-section">
          <h2>📚 Product Catalog</h2>
          <p className="section-info">Select products and quantities, then checkout</p>

          {error && <div className="error-banner">{error}</div>}

          {products.length === 0 ? (
            <div className="no-products">
              <p>No products available</p>
              <button onClick={loadProducts} className="btn btn-secondary">
                Retry
              </button>
            </div>
          ) : (
            <div className="products-grid">
              {products.map(product => (
                <ProductCard
                  key={product.productId}
                  product={product}
                  inCart={cart.has(product.productId)}
                  quantity={cart.get(product.productId)?.quantity || 0}
                  onAddToCart={handleAddToCart}
                />
              ))}
            </div>
          )}
        </section>

        <aside className="cart-section">
          <h3>🛒 Order Summary</h3>

          {cart.size === 0 ? (
            <p className="empty-cart">Your cart is empty</p>
          ) : (
            <>
              <div className="cart-items">
                {Array.from(cart.values()).map(item => (
                  <div key={item.productId} className="cart-item">
                    <div className="cart-item-details">
                      <p className="item-name">{item.product.name}</p>
                      <p className="item-quantity">×{item.quantity} @ ${item.product.price.toFixed(2)}</p>
                    </div>
                    <p className="item-price">${(item.product.price * item.quantity).toFixed(2)}</p>
                  </div>
                ))}
              </div>

              <div className="cart-summary">
                <div className="summary-row">
                  <span>Subtotal:</span>
                  <span>${total.toFixed(2)}</span>
                </div>
                <div className="summary-row">
                  <span>Items:</span>
                  <span>{Array.from(cart.values()).reduce((sum, item) => sum + item.quantity, 0)}</span>
                </div>
                <div className="summary-total">
                  <span>Total:</span>
                  <span className="total-amount">${total.toFixed(2)}</span>
                </div>
              </div>

              <button
                onClick={handlePlaceOrder}
                disabled={placing || cart.size === 0}
                className="btn btn-primary btn-large btn-checkout"
              >
                {placing ? (
                  <>
                    <span className="spinner"></span>
                    Placing Order...
                  </>
                ) : (
                  '✓ Place Order'
                )}
              </button>
            </>
          )}

          <div className="info-box">
            <p>
              <strong>Order Status:</strong> Your order status will be updated in real-time. 
              You'll be able to track it after placing your order.
            </p>
          </div>
        </aside>
      </div>
    </div>
  )
}

/**
 * Product card component
 */
interface ProductCardProps {
  product: Product
  inCart: boolean
  quantity: number
  onAddToCart: (product: Product, quantity: number) => void
}

function ProductCard({ product, inCart, quantity, onAddToCart }: ProductCardProps) {
  const [qty, setQty] = useState(quantity)

  useEffect(() => {
    setQty(quantity)
  }, [quantity])

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newQty = parseInt(e.target.value) || 0
    setQty(newQty)
    onAddToCart(product, newQty)
  }

  return (
    <div className="product-card">
      <div className="product-header">
        <h4>{product.name}</h4>
        <p className="product-price">${product.price.toFixed(2)}</p>
      </div>
      <p className="product-description">{product.description}</p>
      <div className="product-footer">
        <input
          type="number"
          min="0"
          max="100"
          value={qty}
          onChange={handleChange}
          placeholder="Qty"
          className="qty-input"
        />
        {inCart && <span className="in-cart-badge">In Cart</span>}
      </div>
    </div>
  )
}
