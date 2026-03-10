import type { PropsWithChildren, ReactElement } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { useAuth } from '@/auth/useAuth'
import { useOnboardingStatus } from '@/hooks/useOnboardingStatus'
import { useSetupStatus } from '@/hooks/useSetupStatus'

export function ProtectedRoute({ children }: PropsWithChildren): ReactElement {
  const { isAuthenticated, isLoading } = useAuth()
  const location = useLocation()
  const isOnboardingPath = location.pathname === '/onboarding'

  const setup = useSetupStatus()
  const onboarding = useOnboardingStatus(isAuthenticated && !isLoading)

  if (setup.isLoading) {
    return <main>Sprawdzanie konfiguracji systemu…</main>
  }

  if (setup.data && !setup.data.isInitialized) {
    return <Navigate to="/admin-setup" replace />
  }

  if (isLoading) {
    return <main>Sprawdzanie uwierzytelnienia…</main>
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  if (onboarding.isLoading) {
    return <main>Ładowanie…</main>
  }

  if (onboarding.data) {
    if (!onboarding.data.isComplete && !isOnboardingPath) {
      return <Navigate to="/onboarding" replace />
    }

    if (onboarding.data.isComplete && isOnboardingPath) {
      return <Navigate to="/" replace />
    }
  }

  return <>{children}</>
}
