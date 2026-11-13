// @vitest-environment jsdom
import { act, StrictMode, useContext, useEffect, type ContextType } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { User } from 'oidc-client-ts'

const mockManager = {
  getUser: vi.fn<() => Promise<User | null>>(),
  signinRedirect: vi.fn<() => Promise<void>>(),
  signoutRedirect: vi.fn<() => Promise<void>>(),
  signinRedirectCallback: vi.fn<() => Promise<User>>(),
  signinSilent: vi.fn<() => Promise<User | null>>(),
  removeUser: vi.fn<() => Promise<void>>(),
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
  ErrorResponse: class ErrorResponse extends Error {},
}))

vi.mock('@/api/queryClient', () => ({
  queryClient: { clear: vi.fn() },
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

  async function renderContext() {
    const spy = await renderSpy()
    return spy.mock.calls.at(-1)![0]!
  }

  async function renderSpy() {
    mockManager.getUser.mockResolvedValue(null)
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

    return setContextValue
  }

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
    await lastContextValue?.logout()

    expect(mockManager.signinRedirect).toHaveBeenCalledTimes(1)
    expect(mockManager.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: undefined } })
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

  const userOf = (sub: string, token = `token-${sub}`) => ({ access_token: token, expired: false, profile: { sub } }) as unknown as User

  async function mountWith(initialUser: User | null, wrap = false) {
    mockManager.getUser.mockResolvedValue(initialUser)
    const { AuthProvider } = await import('./AuthProvider')
    await act(async () => {
      const tree = <AuthProvider><div /></AuthProvider>
      root.render(wrap ? <StrictMode>{tree}</StrictMode> : tree)
      await flushPromises()
    })
  }

  const loadedHandler = () => mockManager.events.addUserLoaded.mock.calls.at(-1)![0] as (user: User) => void
  const unloadedHandler = () => mockManager.events.addUserUnloaded.mock.calls.at(-1)![0] as () => void

  it('leaves renewal to the library and builds one manager under StrictMode', async () => {
    const { UserManager } = await import('oidc-client-ts')
    vi.mocked(UserManager).mockClear()

    await mountWith(userOf('a'), true)

    expect(UserManager).toHaveBeenCalledTimes(1)
    expect(mockManager.events.addAccessTokenExpiring).not.toHaveBeenCalled()
    expect(mockManager.events.addAccessTokenExpired).not.toHaveBeenCalled()
    expect(mockManager.signinSilent).not.toHaveBeenCalled()
  })

  it('makes one signinSilent for ten parallel renew calls', async () => {
    const { renewSession } = await import('./accessToken')
    let resolve!: (user: User) => void
    mockManager.signinSilent.mockReturnValue(new Promise((r) => { resolve = r }))
    await mountWith(userOf('a'))

    const calls = Array.from({ length: 10 }, () => renewSession())
    resolve(userOf('a', 'renewed'))

    expect(await Promise.all(calls)).toEqual(Array(10).fill('renewed'))
    expect(mockManager.signinSilent).toHaveBeenCalledTimes(1)
  })

  it('removes the user when Keycloak rejects the renew', async () => {
    const { ErrorResponse } = await import('oidc-client-ts')
    const { renewSession } = await import('./accessToken')
    mockManager.signinSilent.mockRejectedValue(new (ErrorResponse as unknown as new () => Error)())
    await mountWith(userOf('a'))

    expect(await renewSession()).toBeNull()
    expect(mockManager.removeUser).toHaveBeenCalledTimes(1)
  })

  it('keeps the user when the renew fails on the network', async () => {
    const { renewSession } = await import('./accessToken')
    mockManager.signinSilent.mockRejectedValue(new Error('Failed to fetch'))
    await mountWith(userOf('a'))

    expect(await renewSession()).toBeNull()
    expect(mockManager.removeUser).not.toHaveBeenCalled()
  })

  it('ends the session through removeUser and marks it ended until the next login', async () => {
    const { endSession } = await import('./accessToken')
    const context = () => setContextValue.mock.calls.at(-1)![0]!
    const setContextValue = await renderSpy()
    expect(context().sessionEnded).toBe(false)

    await act(async () => endSession())

    expect(mockManager.removeUser).toHaveBeenCalledTimes(1)
    expect(context().sessionEnded).toBe(true)

    await act(async () => context().login('/telefon'))
    expect(context().sessionEnded).toBe(false)
    expect(mockManager.signinRedirect).toHaveBeenCalledWith({ state: { returnTo: '/telefon' } })
  })

  it('clears the query cache on sign-out and user switch, not on renew or sign-in', async () => {
    const { queryClient } = await import('@/api/queryClient')
    await mountWith(null)

    await act(async () => loadedHandler()(userOf('a')))
    expect(queryClient.clear).not.toHaveBeenCalled()

    await act(async () => loadedHandler()(userOf('a', 'renewed')))
    expect(queryClient.clear).not.toHaveBeenCalled()

    await act(async () => loadedHandler()(userOf('b')))
    expect(queryClient.clear).toHaveBeenCalledTimes(1)

    await act(async () => unloadedHandler()())
    expect(queryClient.clear).toHaveBeenCalledTimes(2)
  })

  it('returns the requested route from the OIDC callback state', async () => {
    const context = await renderContext()

    mockManager.signinRedirectCallback.mockResolvedValueOnce({ ...userOf('a'), state: { returnTo: '/firmy/1?x=1' } } as User)
    expect(await context.handleOidcCallback()).toBe('/firmy/1?x=1')

    mockManager.signinRedirectCallback.mockResolvedValueOnce(userOf('a'))
    expect(await context.handleOidcCallback()).toBe('/')
  })
})
