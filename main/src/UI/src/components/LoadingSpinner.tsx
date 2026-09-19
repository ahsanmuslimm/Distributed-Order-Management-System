/**
 * Loading Spinner Component
 * 
 * Displays a loading indicator with optional message
 */
interface LoadingSpinnerProps {
  message?: string
}

export default function LoadingSpinner({ message = 'Loading...' }: LoadingSpinnerProps) {
  return (
    <div className="loading-container">
      <div className="spinner-box">
        <div className="spinner"></div>
        <p>{message}</p>
      </div>
    </div>
  )
}
