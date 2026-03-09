import { useEffect, useState, type ReactElement } from 'react'
import { useNavigate } from 'react-router-dom'

import { useAuth } from '@/auth/useAuth'

let isProcessingCallback = false

export function Callback(): ReactElement {
  const { handleOidcCallback } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (isProcessingCallback) {
      return
    }

    isProcessingCallback = true
    let isMounted = true

    async function processCallback(): Promise<void> {
      try {
        await handleOidcCallback()
        if (isMounted) {
          navigate('/', { replace: true })
        }
      } catch (callbackError) {
        if (!isMounted) {
          return
        }

        const message = callbackError instanceof Error ? callbackError.message : 'Unknown authentication error'
        setError(message)
      } finally {
        isProcessingCallback = false
      }
    }

    void processCallback()
    return () => {
      isMounted = false
    }
  }, [handleOidcCallback, navigate])

  if (error) {
    return (
      <main className="auth-page">
        <div className="auth-card">
          <div className="auth-card__icon auth-card__icon--error">x</div>
          <h1 className="auth-card__title">Authentication failed</h1>
          <p className="auth-card__desc">{error}</p>
          <a href="/" className="auth-card__link">Back to login</a>
        </div>
      </main>
    )
  }

  return (
    <main className="auth-page">
      <div className="auth-card">
        <div className="auth-spinner" aria-hidden="true" />
        <h1 className="auth-card__title">Signing in...</h1>
        <p className="auth-card__desc">Completing secure sign-in, please wait.</p>
      </div>
    </main>
  )
}
