// @vitest-environment jsdom
import { act, useContext, useEffect, type ContextType } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { User } from 'oidc-client-ts'

const mockManager = {
  getUser: vi.fn<() => Promise<User | null>>(),
  signinRedirect: vi.fn<() => Promise<void>>(),
  signoutRedirect: vi.fn<() => Promise<void>>(),
  signinRedirectCallback: vi.fn<() => Promise<void>>(),
  signinSilent: vi.fn<() => Promise<User | null>>(),
  startSilentRenew: vi.fn<() => void>(),
  stopSilentRenew: vi.fn<() => void>(),
  events: {
    addUserLoaded: vi.fn(),
    removeUserLoaded: vi.fn(),
    addUserUnloaded: vi.fn(),
    removeUserUnloaded: vi.fn(),
    addAccessTokenExpiring: vi.fn(),
    removeAccessTokenExpiring: vi.fn(),
    addAccessTokenExpired: vi.fn(),
    removeAccessTokenExpired: vi.fn(),
  },
}

vi.mock('oidc-client-ts', () => ({
  UserManager: vi.fn(function UserManager() {
    return mockManager
  }),
}))

vi.mock('./config', () => ({
  oidcConfig: {
    authority: 'http://localhost/auth/realms/openksef',
    client_id: 'openksef-portal-web',
  },
}))

function flushPromises() {
  return new Promise<void>((resolve) => {
    setTimeout(() => resolve(), 0)
  })
}

describe('AuthProvider', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    window.history.replaceState({}, '', '/')
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('exposes login/logout and access token from current user', async () => {
    mockManager.getUser.mockResolvedValue({ access_token: 'access-token', expired: false } as User)

    const { AuthProvider } = await import('./AuthProvider')
    const { AuthContext } = await import('./AuthContext')

    const setContextValue = vi.fn<(value: ContextType<typeof AuthContext>) => void>()
    function Consumer() {
      const value = useContext(AuthContext)
      useEffect(() => {
        setContextValue(value)
      }, [value])
      return null
    }

    await act(async () => {
      root.render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>,
      )
      await flushPromises()
    })

    const lastContextValue = setContextValue.mock.calls.at(-1)?.[0]
    expect(lastContextValue).toBeDefined()
    expect(lastContextValue?.isAuthenticated).toBe(true)
    expect(await lastContextValue?.getAccessToken()).toBe('access-token')

    await lastContextValue?.login()
    await lastContextValue?.login('signup')
    await lastContextValue?.logout()

    expect(mockManager.signinRedirect).toHaveBeenCalledTimes(2)
    expect(mockManager.signinRedirect).toHaveBeenNthCalledWith(2, {
      extraQueryParams: {
        kc_action: 'register',
      },
    })
    expect(mockManager.startSilentRenew).toHaveBeenCalledTimes(1)
    expect(mockManager.signoutRedirect).toHaveBeenCalledTimes(1)
  })

  it('does not process callback route directly', async () => {
    mockManager.getUser.mockResolvedValue(null)
    window.history.replaceState({}, '', '/callback')

    const { AuthProvider } = await import('./AuthProvider')

    await act(async () => {
      root.render(<AuthProvider><div>test</div></AuthProvider>)
      await flushPromises()
    })

    expect(mockManager.signinRedirectCallback).not.toHaveBeenCalled()
    expect(window.location.pathname).toBe('/callback')
  })
})
