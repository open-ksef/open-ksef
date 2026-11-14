import { useCallback, useEffect, useRef, useState, type ReactElement } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { useAuth } from '@/auth/useAuth'
import { BrandMark } from '@/components/BrandMark'

export function LoginPage(): ReactElement {
  const { isAuthenticated, isLoading, sessionEnded, login } = useAuth()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? new URLSearchParams(location.search).get('returnTo')
  const returnTo = from?.startsWith('/') && !from.startsWith('//') && !from.startsWith('/\\') ? from : '/'
  const [error, setError] = useState<string | null>(null)
  const [isRedirecting, setIsRedirecting] = useState(false)
  const autoRedirected = useRef(false)

  const handleLogin = useCallback(async (): Promise<void> => {
    setError(null)
    setIsRedirecting(true)
    try {
      await login(returnTo)
    } catch {
      setError('Nie udało się otworzyć strony logowania. Sprawdź połączenie i spróbuj ponownie.')
      setIsRedirecting(false)
    }
  }, [login, returnTo])

  useEffect(() => {
    if (isLoading || isAuthenticated || sessionEnded || autoRedirected.current) return
    autoRedirected.current = true
    void handleLogin()
  }, [handleLogin, isAuthenticated, isLoading, sessionEnded])

  if (isAuthenticated) {
    return <Navigate to={returnTo} replace />
  }

  return (
    <main className="auth-page">
      <div className="auth-card">
        <div className="auth-logo">
          <BrandMark size={40} />
        </div>
        <h1 className="auth-card__title">OpenKSeF</h1>
        <p className="auth-card__desc">Zaloguj się do portalu OpenKSeF</p>

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
            disabled={isLoading || isRedirecting}
            data-testid="login-submit"
          >
            {isLoading || isRedirecting ? 'Przekierowanie…' : 'Zaloguj się'}
          </button>
        </div>
      </div>
    </main>
  )
}
