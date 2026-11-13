import { useCallback, useEffect, useMemo, useRef, useState, type PropsWithChildren, type ReactElement } from 'react'
import { ErrorResponse, UserManager, type User } from 'oidc-client-ts'

import { queryClient } from '@/api/queryClient'
import { AuthContext } from './AuthContext'
import { clearAccessTokenProvider, clearSessionHandlers, setAccessTokenProvider, setSessionHandlers } from './accessToken'
import { oidcConfig } from './config'

export function AuthProvider({ children }: PropsWithChildren): ReactElement {
  const managerRef = useRef<UserManager | null>(null)
  if (managerRef.current === null) {
    managerRef.current = new UserManager(oidcConfig)
  }

  // Ref, not useState initializer: StrictMode would build a second UserManager with its own silent-renew timer.
  // eslint-disable-next-line react-hooks/refs
  const manager = managerRef.current
  const userRef = useRef<User | null>(null)
  const renewInFlight = useRef<Promise<string | null> | null>(null)
  const [user, setUserState] = useState<User | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [sessionEnded, setSessionEnded] = useState(false)

  const setUser = useCallback((nextUser: User | null) => {
    const previous = userRef.current
    if (previous && previous.profile.sub !== nextUser?.profile.sub) queryClient.clear()
    userRef.current = nextUser
    setAccessTokenProvider(() => nextUser?.access_token ?? null)
    setUserState(nextUser)
  }, [])

  const renew = useCallback(() => {
    renewInFlight.current ??= manager.signinSilent()
      .then((renewed) => renewed?.access_token ?? null)
      .catch(async (error: unknown) => {
        if (error instanceof ErrorResponse) await manager.removeUser()
        return null
      })
      .finally(() => {
        renewInFlight.current = null
      })
    return renewInFlight.current
  }, [manager])

  useEffect(() => {
    setAccessTokenProvider(() => userRef.current?.access_token ?? null)
    setSessionHandlers({
      renew,
      end: async () => {
        setSessionEnded(true)
        await manager.removeUser()
      },
    })
    return () => {
      clearAccessTokenProvider()
      clearSessionHandlers()
    }
    // eslint-disable-next-line react-hooks/refs
  }, [manager, renew])

  useEffect(() => {
    let isMounted = true

    async function initializeAuth(): Promise<void> {
      try {
        const currentUser = await manager.getUser()
        if (isMounted) {
          setUser(currentUser)
        }
      } finally {
        if (isMounted) {
          setIsLoading(false)
        }
      }
    }

    const onUserLoaded = (loadedUser: User) => {
      if (isMounted) {
        setUser(loadedUser)
      }
    }

    const onUserUnloaded = () => {
      if (isMounted) {
        setUser(null)
      }
    }

    manager.events.addUserLoaded(onUserLoaded)
    manager.events.addUserUnloaded(onUserUnloaded)
    void initializeAuth()

    return () => {
      isMounted = false
      manager.events.removeUserLoaded(onUserLoaded)
      manager.events.removeUserUnloaded(onUserUnloaded)
    }
    // eslint-disable-next-line react-hooks/refs
  }, [manager, setUser])

  const login = useCallback(async (returnTo?: string) => {
    setSessionEnded(false)
    await manager.signinRedirect({ state: { returnTo } })
  }, [manager])

  const logout = useCallback(async () => {
    await manager.signoutRedirect()
  }, [manager])

  const getAccessToken = useCallback(async () => user?.access_token ?? null, [user])

  const handleOidcCallback = useCallback(async () => {
    const callbackUser = await manager.signinRedirectCallback()
    setUser(callbackUser)
    const returnTo = (callbackUser.state as { returnTo?: string } | undefined)?.returnTo
    return returnTo?.startsWith('/') && !returnTo.startsWith('//') && !returnTo.startsWith('/\\') ? returnTo : '/'
  }, [manager, setUser])

  const value = useMemo(
    () => ({
      user,
      isLoading,
      isAuthenticated: Boolean(user && !user.expired),
      sessionEnded,
      login,
      logout,
      getAccessToken,
      handleOidcCallback,
    }),
    [getAccessToken, handleOidcCallback, isLoading, login, logout, sessionEnded, user],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
