import { useState } from 'react'
import { BrowserRouter as Router, Routes, Route, Link } from 'react-router-dom'
import CheckoutPage from './pages/CheckoutPage'
import OrderStatusPage from './pages/OrderStatusPage'
import './App.css'

/**
 * Root application component
 * 
 * Routes:
 * - / → Checkout page (place new orders)
 * - /order/:orderId → Order status page (track order)
 */
function App() {
  return (
    <Router>
      <div className="app">
        <header className="header">
          <div className="header-content">
            <h1>
              <Link to="/" className="logo-link">
                📦 Order Management System
              </Link>
            </h1>
            <p className="subtitle">Distributed order processing with automatic compensation</p>
          </div>
        </header>

        <main className="main-content">
          <Routes>
            <Route path="/" element={<CheckoutPage />} />
            <Route path="/order/:orderId" element={<OrderStatusPage />} />
            <Route
              path="*"
              element={
                <div className="error-page">
                  <h2>404 - Page Not Found</h2>
                  <p><Link to="/">Back to checkout</Link></p>
                </div>
              }
            />
          </Routes>
        </main>

        <footer className="footer">
          <p>API Gateway: <code>http://localhost:5000</code></p>
          <p>Jaeger UI: <a href="http://localhost:16686" target="_blank" rel="noopener noreferrer">http://localhost:16686</a></p>
        </footer>
      </div>
    </Router>
  )
}

export default App
