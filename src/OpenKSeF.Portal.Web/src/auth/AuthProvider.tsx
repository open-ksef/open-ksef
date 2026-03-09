import { useCallback, useEffect, useMemo, useRef, useState, type PropsWithChildren, type ReactElement } from 'react'
import { UserManager, type User } from 'oidc-client-ts'

import { AuthContext, type LoginAction, type RegisterData } from './AuthContext'
import { clearAccessTokenProvider, setAccessTokenProvider } from './accessToken'
import { oidcConfig } from './config'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api'

export function AuthProvider({ children }: PropsWithChildren): ReactElement {
  const managerRef = useRef<UserManager | null>(null)
  if (managerRef.current === null) {
    managerRef.current = new UserManager(oidcConfig)
  }

  const manager = managerRef.current
  const userRef = useRef<User | null>(null)
  const [user, setUserState] = useState<User | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  const setUser = useCallback((nextUser: User | null) => {
    userRef.current = nextUser
    setAccessTokenProvider(() => nextUser?.access_token ?? null)
    setUserState(nextUser)
  }, [])

  useEffect(() => {
    setAccessTokenProvider(() => userRef.current?.access_token ?? null)
    return () => {
      clearAccessTokenProvider()
    }
  }, [])

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

    async function renewSilently(): Promise<void> {
      try {
        const renewedUser = await manager.signinSilent()
        if (isMounted) {
          setUser(renewedUser)
        }
      } catch {
        if (isMounted) {
          setUser(null)
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

    const onAccessTokenExpiring = () => {
      void renewSilently()
    }

    const onAccessTokenExpired = () => {
      void renewSilently()
    }

    manager.events.addUserLoaded(onUserLoaded)
    manager.events.addUserUnloaded(onUserUnloaded)
    manager.events.addAccessTokenExpiring(onAccessTokenExpiring)
    manager.events.addAccessTokenExpired(onAccessTokenExpired)
    manager.startSilentRenew()
    void initializeAuth()

    return () => {
      isMounted = false
      manager.events.removeUserLoaded(onUserLoaded)
      manager.events.removeUserUnloaded(onUserUnloaded)
      manager.events.removeAccessTokenExpiring(onAccessTokenExpiring)
      manager.events.removeAccessTokenExpired(onAccessTokenExpired)
      manager.stopSilentRenew()
    }
  }, [manager, setUser])

  const login = useCallback(async (action: LoginAction = 'signin') => {
    if (action === 'signup') {
      await manager.signinRedirect({
        extraQueryParams: {
          kc_action: 'register',
        },
      })
      return
    }

    await manager.signinRedirect()
  }, [manager])

  const loginWithCredentials = useCallback(async (username: string, password: string) => {
    await manager.signinResourceOwnerCredentials({
      username,
      password,
      skipUserInfo: false,
    })
  }, [manager])

  const loginWithGoogle = useCallback(async () => {
    await manager.signinRedirect({
      extraQueryParams: { kc_idp_hint: 'google' },
    })
  }, [manager])

  const register = useCallback(async (data: RegisterData) => {
    const response = await fetch(`${API_BASE_URL}/account/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    })

    if (!response.ok) {
      const body = await response.json().catch(() => ({})) as Record<string, unknown>
      throw new Error((body.error as string) ?? 'Registration failed.')
    }

    await manager.signinResourceOwnerCredentials({
      username: data.email,
      password: data.password,
      skipUserInfo: false,
    })
  }, [manager])

  const logout = useCallback(async () => {
    await manager.signoutRedirect()
  }, [manager])

  const getAccessToken = useCallback(async () => user?.access_token ?? null, [user])

  const handleOidcCallback = useCallback(async () => {
    const callbackUser = await manager.signinRedirectCallback()
    setUser(callbackUser)
  }, [manager, setUser])

  const value = useMemo(
    () => ({
      user,
      isLoading,
      isAuthenticated: Boolean(user && !user.expired),
      login,
      loginWithCredentials,
      loginWithGoogle,
      register,
      logout,
      getAccessToken,
      handleOidcCallback,
    }),
    [getAccessToken, handleOidcCallback, isLoading, login, loginWithCredentials, loginWithGoogle, register, logout, user],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
