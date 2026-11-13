import { useState, type ReactElement } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { useAuth } from '@/auth/useAuth'
import { BrandMark } from '@/components/BrandMark'

export function LoginPage(): ReactElement {
  const { isAuthenticated, isLoading, login } = useAuth()
  const from = (useLocation().state as { from?: string } | null)?.from
  const returnTo = from?.startsWith('/') && !from.startsWith('//') ? from : '/'
  const [error, setError] = useState<string | null>(null)
  const [isRedirecting, setIsRedirecting] = useState(false)

  if (isLoading) {
    return <main className="auth-page"><div className="auth-card">Sprawdzanie uwierzytelnienia…</div></main>
  }

  if (isAuthenticated) {
    return <Navigate to={returnTo} replace />
  }

  async function handleLogin(): Promise<void> {
    setError(null)
    setIsRedirecting(true)
    try {
      await login(returnTo)
    } catch {
      setError('Nie udało się otworzyć strony logowania. Sprawdź połączenie i spróbuj ponownie.')
      setIsRedirecting(false)
    }
  }

  return (
    <main className="auth-page">
      <div className="auth-card">
        <div className="auth-logo">
          <BrandMark size={40} />
        </div>
        <h1 className="auth-card__title">OpenKSeF</h1>
        <p className="auth-card__desc">Logowanie i zakładanie konta odbywa się na stronie logowania OpenKSeF.</p>

        {error && (
          <div className="ui-form-error auth-card__error" role="alert">
            <span className="ui-form-error__icon" aria-hidden="true">!</span>
            {error}
          </div>
        )}

        <div className="auth-card__actions">
          <button
            type="button"
            className="ui-button ui-button--primary auth-card__btn"
            onClick={() => void handleLogin()}
            disabled={isRedirecting}
            data-testid="login-submit"
          >
            {isRedirecting ? 'Przekierowanie…' : 'Zaloguj się'}
          </button>
        </div>
      </div>
    </main>
  )
}
