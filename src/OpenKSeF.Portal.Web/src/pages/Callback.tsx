import { useEffect, useState, type ReactElement } from 'react'
import { useNavigate } from 'react-router-dom'

import { useAuth } from '@/auth/useAuth'

let isProcessingCallback = false
const RETRIED_KEY = 'openksef.callback.retried'

function isStaleCallback(error: unknown): boolean {
  return (error as { error?: string } | null)?.error === 'invalid_grant'
    || (error instanceof Error && error.message.includes('No matching state'))
}

export function Callback(): ReactElement {
  const { handleOidcCallback } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (isProcessingCallback) {
      return
    }

    isProcessingCallback = true

    async function processCallback(): Promise<void> {
      try {
        navigate(await handleOidcCallback(), { replace: true })
        sessionStorage.removeItem(RETRIED_KEY)
      } catch (callbackError) {
        // A code or state used up by a double callback or back/forward: start a fresh login once instead of showing Keycloak's English error.
        if (isStaleCallback(callbackError) && !sessionStorage.getItem(RETRIED_KEY)) {
          sessionStorage.setItem(RETRIED_KEY, '1')
          navigate('/login', { replace: true })
          return
        }
        sessionStorage.removeItem(RETRIED_KEY)
        setError('Nie udało się dokończyć logowania. Zaloguj się ponownie.')
      } finally {
        isProcessingCallback = false
      }
    }

    void processCallback()
  }, [handleOidcCallback, navigate])

  if (error) {
    return (
      <main className="auth-page">
        <div className="auth-card">
          <div className="auth-card__icon auth-card__icon--error">x</div>
          <h1 className="auth-card__title">Logowanie nie powiodło się</h1>
          <p className="auth-card__desc" role="alert">{error}</p>
          <a href="/login" className="auth-card__link">Wróć do logowania</a>
        </div>
      </main>
    )
  }

  return (
    <main className="auth-page">
      <div className="auth-card">
        <div className="auth-spinner" aria-hidden="true" />
        <h1 className="auth-card__title">Logowanie…</h1>
        <p className="auth-card__desc">Kończymy bezpieczne logowanie, chwilę.</p>
      </div>
    </main>
  )
}
