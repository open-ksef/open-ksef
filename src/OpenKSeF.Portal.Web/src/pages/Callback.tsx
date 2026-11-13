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

    async function processCallback(): Promise<void> {
      try {
        navigate(await handleOidcCallback(), { replace: true })
      } catch (callbackError) {
        setError(callbackError instanceof Error && callbackError.message ? callbackError.message : 'Nieznany błąd logowania.')
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
