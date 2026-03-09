import { useEffect, useState, type ReactElement } from 'react'
import { UserManager } from 'oidc-client-ts'

import { oidcConfig } from '@/auth/config'

export function SilentCallbackPage(): ReactElement {
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let isMounted = true

    async function processSilentCallback(): Promise<void> {
      try {
        const manager = new UserManager(oidcConfig)
        await manager.signinSilentCallback()
      } catch (callbackError) {
        if (!isMounted) {
          return
        }

        const message = callbackError instanceof Error ? callbackError.message : 'Silent renew failed'
        setError(message)
      }
    }

    void processSilentCallback()
    return () => {
      isMounted = false
    }
  }, [])

  if (error) {
    return <main>Session refresh failed: {error}</main>
  }

  return <main>Refreshing session...</main>
}
