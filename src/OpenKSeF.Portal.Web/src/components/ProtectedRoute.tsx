import { useEffect, useRef, useState, type PropsWithChildren, type ReactElement } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { INVITE_PATH, INVITE_TOKEN_KEY } from '@/auth/inviteToken'
import { useAuth } from '@/auth/useAuth'
import { useOnboardingStatus } from '@/hooks/useOnboardingStatus'
import { useSetupStatus } from '@/hooks/useSetupStatus'

export function ProtectedRoute({ children }: PropsWithChildren): ReactElement {
  const { isAuthenticated, isLoading, sessionEnded, login } = useAuth()
  const location = useLocation()
  const isInvitePath = location.pathname === INVITE_PATH
  const returnTo = isInvitePath ? INVITE_PATH : location.pathname + location.search + location.hash
  const redirecting = useRef(false)
  const [redirectFailed, setRedirectFailed] = useState(false)
  const isOnboardingPath = location.pathname === '/onboarding'

  const setup = useSetupStatus()
  const onboarding = useOnboardingStatus(isAuthenticated && !isLoading)
  const mustLogIn = !setup.isLoading && setup.data?.isInitialized !== false && !isLoading && !isAuthenticated && !sessionEnded

  useEffect(() => {
    if (isInvitePath && !isAuthenticated && location.hash.length > 1) sessionStorage.setItem(INVITE_TOKEN_KEY, location.hash.slice(1))
  }, [isAuthenticated, isInvitePath, location.hash])

  useEffect(() => {
    if (!mustLogIn || redirecting.current) return
    redirecting.current = true
    login(returnTo).catch(() => setRedirectFailed(true))
  }, [login, mustLogIn, returnTo])

  if (setup.isLoading) {
    return <main>Sprawdzanie konfiguracji systemu…</main>
  }

  if (setup.data && !setup.data.isInitialized) {
    return <Navigate to="/admin-setup" replace />
  }

  if (isLoading) {
    return <main>Sprawdzanie uwierzytelnienia…</main>
  }

  if ((sessionEnded || redirectFailed) && !isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: returnTo }} />
  }

  if (!isAuthenticated) {
    return <main>Przekierowanie do logowania…</main>
  }

  if (onboarding.isLoading) {
    return <main>Ładowanie…</main>
  }

  if (onboarding.data) {
    if (!onboarding.data.isComplete && !isOnboardingPath && !isInvitePath) {
      return <Navigate to="/onboarding" replace />
    }

    if (onboarding.data.isComplete && isOnboardingPath) {
      return <Navigate to="/" replace />
    }
  }

  return <>{children}</>
}
