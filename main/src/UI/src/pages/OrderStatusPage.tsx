import { useState, useEffect } from 'react'
import { useParams, Link } from 'react-router-dom'
import { apiService } from '../services/api'
import { OrderStatus } from '../types/models'
import LoadingSpinner from '../components/LoadingSpinner'
import '../styles/order-status.css'

/**
 * Order Status Page
 * 
 * Displays:
 * 1. Current order status (Pending, Confirmed, or Failed)
 * 2. Order details (ID, customer, total, items)
 * 3. Real-time status updates (polls every 2 seconds)
 * 4. Compensation indication if payment failed
 */
export default function OrderStatusPage() {
  const { orderId } = useParams<{ orderId: string }>()
  
  // State
  const [order, setOrder] = useState<OrderStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [isTerminal, setIsTerminal] = useState(false)
  const [pollCount, setPollCount] = useState(0)

  // Load order on mount and set up polling
  useEffect(() => {
    if (!orderId) {
      setError('No order ID provided')
      setLoading(false)
      return
    }

    // Initial load
    loadOrder()

    // Set up polling (every 2 seconds until terminal state)
    const interval = setInterval(() => {
      loadOrder()
    }, 2000)

    return () => clearInterval(interval)
  }, [orderId])

  // Load order details
  const loadOrder = async () => {
    if (!orderId) return

    try {
      const orderData = await apiService.getOrderStatus(orderId)
      setOrder(orderData)
      setError(null)
      setPollCount(c => c + 1)

      // Check if terminal state reached
      if (orderData.status === 'Confirmed' || orderData.status === 'Failed') {
        setIsTerminal(true)
      }

      setLoading(false)
    } catch (err: any) {
      setError(err.message || 'Failed to load order')
      setLoading(false)
      console.error('Error loading order:', err)
    }
  }

  // Get status badge color
  const getStatusColor = (status: string): string => {
    switch (status) {
      case 'Pending':
        return 'status-pending'
      case 'Confirmed':
        return 'status-confirmed'
      case 'Failed':
        return 'status-failed'
      default:
        return 'status-unknown'
    }
  }

  // Get status emoji
  const getStatusEmoji = (status: string): string => {
    switch (status) {
      case 'Pending':
        return '⏳'
      case 'Confirmed':
        return '✅'
      case 'Failed':
        return '❌'
      default:
        return '❓'
    }
  }

  // Loading state
  if (loading) {
    return <LoadingSpinner message={`Loading order ${orderId}...`} />
  }

  // Error state
  if (error) {
    return (
      <div className="order-status-container">
        <div className="error-box">
          <h2>❌ Error</h2>
          <p>{error}</p>
          <Link to="/" className="btn btn-primary">
            Back to Checkout
          </Link>
        </div>
      </div>
    )
  }

  // Not found
  if (!order) {
    return (
      <div className="order-status-container">
        <div className="error-box">
          <h2>❌ Order Not Found</h2>
          <p>Order {orderId} could not be found</p>
          <Link to="/" className="btn btn-primary">
            Back to Checkout
          </Link>
        </div>
      </div>
    )
  }

  return (
    <div className="order-status-container">
      <div className="order-status-content">
        {/* Status Header */}
        <div className={`status-header ${getStatusColor(order.status)}`}>
          <h2>
            {getStatusEmoji(order.status)} Order {order.status}
          </h2>
          <p className="order-id-display">Order ID: <code>{order.orderId}</code></p>
          
          {isTerminal && (
            <p className="status-note">
              {order.status === 'Confirmed'
                ? '✅ Your order has been fully processed'
                : '❌ Your order could not be completed'}
            </p>
          )}

          {!isTerminal && (
            <p className="status-note loading-note">
              ⏳ Processing... (checked {pollCount} times)
            </p>
          )}
        </div>

        {/* Order Details */}
        <section className="order-details-section">
          <h3>Order Details</h3>
          <div className="details-grid">
            <div className="detail-item">
              <label>Order ID</label>
              <code className="detail-value">{order.orderId}</code>
            </div>
            <div className="detail-item">
              <label>Customer ID</label>
              <code className="detail-value">{order.customerId}</code>
            </div>
            <div className="detail-item">
              <label>Status</label>
              <span className={`status-badge ${getStatusColor(order.status)}`}>
                {order.status}
              </span>
            </div>
            <div className="detail-item">
              <label>Total Amount</label>
              <strong className="detail-value">${order.totalAmount.toFixed(2)}</strong>
            </div>
            <div className="detail-item">
              <label>Created At</label>
              <span className="detail-value">{new Date(order.createdAt).toLocaleString()}</span>
            </div>
            <div className="detail-item">
              <label>Item Count</label>
              <span className="detail-value">{order.items.length}</span>
            </div>
          </div>
        </section>

        {/* Order Items */}
        <section className="order-items-section">
          <h3>Items</h3>
          {order.items.length === 0 ? (
            <p className="no-items">No items in this order</p>
          ) : (
            <div className="items-table">
              <div className="table-header">
                <div className="col-product">Product ID</div>
                <div className="col-quantity">Quantity</div>
                <div className="col-price">Unit Price</div>
                <div className="col-total">Total</div>
              </div>
              {order.items.map(item => (
                <div key={item.orderItemId} className="table-row">
                  <div className="col-product">
                    <code>{item.productId}</code>
                  </div>
                  <div className="col-quantity">{item.quantity}</div>
                  <div className="col-price">${item.unitPrice.toFixed(2)}</div>
                  <div className="col-total">
                    <strong>${(item.quantity * item.unitPrice).toFixed(2)}</strong>
                  </div>
                </div>
              ))}
            </div>
          )}
        </section>

        {/* Status Information */}
        <section className="status-info-section">
          <h3>Status Information</h3>
          <div className="info-card">
            {order.status === 'Pending' && (
              <>
                <h4>⏳ Order is Being Processed</h4>
                <p>Your order is currently being processed through our distributed system:</p>
                <ol className="process-steps">
                  <li><strong>Inventory Check</strong>: Verifying stock availability</li>
                  <li><strong>Payment Processing</strong>: Processing payment</li>
                  <li><strong>Order Confirmation</strong>: Finalizing order</li>
                  <li><strong>Notification</strong>: Sending confirmation email</li>
                </ol>
                <p className="info-note">
                  This page automatically updates every 2 seconds. 
                  Your order will transition to "Confirmed" or "Failed" shortly.
                </p>
              </>
            )}

            {order.status === 'Confirmed' && (
              <>
                <h4>✅ Order Confirmed!</h4>
                <p>Your order has been successfully processed:</p>
                <ul className="check-list">
                  <li>✓ Inventory reserved</li>
                  <li>✓ Payment processed</li>
                  <li>✓ Order confirmed</li>
                  <li>✓ Notification sent</li>
                </ul>
                <p className="info-note">
                  Your order will be prepared for shipment shortly.
                </p>
              </>
            )}

            {order.status === 'Failed' && (
              <>
                <h4>❌ Order Could Not Be Completed</h4>
                <p>Unfortunately, your order could not be processed. This may be due to:</p>
                <ul className="failure-reasons">
                  <li>Payment failure or cancellation</li>
                  <li>Insufficient inventory</li>
                  <li>Technical issue in processing</li>
                </ul>
                <p className="info-note compensation-note">
                  <strong>Automatic Compensation:</strong> If inventory was reserved, it has been automatically released.
                </p>
              </>
            )}
          </div>
        </section>

        {/* Debug Info */}
        <section className="debug-section">
          <h3>🔍 Debug Information</h3>
          <div className="debug-box">
            <p>
              <strong>Polls Completed:</strong> {pollCount}
            </p>
            <p>
              <strong>Is Terminal State:</strong> {isTerminal ? 'Yes' : 'No'}
            </p>
            <p>
              <strong>Last Updated:</strong> {new Date().toLocaleTimeString()}
            </p>
            <p>
              <strong>Trace ID:</strong> {order.orderId} 
              {' '}
              <a 
                href={apiService.getTraceUrl(order.orderId)}
                target="_blank"
                rel="noopener noreferrer"
                className="debug-link"
              >
                View in Jaeger →
              </a>
            </p>
          </div>
        </section>

        {/* Actions */}
        <div className="order-actions">
          <Link to="/" className="btn btn-primary">
            ← Back to Checkout
          </Link>
          <button onClick={loadOrder} className="btn btn-secondary">
            🔄 Refresh Status
          </button>
        </div>
      </div>
    </div>
  )
}
