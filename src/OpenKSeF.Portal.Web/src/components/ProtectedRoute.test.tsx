// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuth } from '@/auth/useAuth'
import { getSetupStatus } from '@/api/endpoints/system'
import { getOnboardingStatus } from '@/api/endpoints/account'
import { ProtectedRoute } from './ProtectedRoute'

vi.mock('@/auth/useAuth', () => ({
  useAuth: vi.fn(),
}))

vi.mock('@/api/endpoints/system', () => ({
  getSetupStatus: vi.fn(),
}))

vi.mock('@/api/endpoints/account', () => ({
  getOnboardingStatus: vi.fn(),
}))

function flushPromises() {
  return new Promise<void>((resolve) => setTimeout(resolve, 0))
}

describe('ProtectedRoute', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true

    vi.mocked(getSetupStatus).mockResolvedValue({ isInitialized: true })
    vi.mocked(getOnboardingStatus).mockResolvedValue({
      isComplete: true,
      hasTenant: true,
      hasCredential: true,
      firstTenantId: 'tenant-1',
    })

    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders children when user is authenticated', async () => {
    vi.mocked(useAuth).mockReturnValue({
      user: null,
      isLoading: false,
      isAuthenticated: true,
      login: vi.fn(),
      sessionEnded: false,
      startAction: vi.fn(),
      logout: vi.fn(),
      getAccessToken: vi.fn(),
      handleOidcCallback: vi.fn(),
    })

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={['/']}>
            <Routes>
              <Route path="/" element={<ProtectedRoute><div>Protected content</div></ProtectedRoute>} />
              <Route path="/login" element={<div>Login page</div>} />
            </Routes>
          </MemoryRouter>
        </QueryClientProvider>,
      )
    })

    await act(async () => {
      await flushPromises()
    })

    expect(container.textContent).toContain('Protected content')
  })

  async function renderAt(path: string, auth: Partial<ReturnType<typeof useAuth>>) {
    vi.mocked(useAuth).mockReturnValue({ isLoading: false, isAuthenticated: false, sessionEnded: false, ...auth } as ReturnType<typeof useAuth>)
    function LoginProbe() {
      return <div>login from={(useLocation().state as { from: string }).from}</div>
    }
    await act(async () => {
      root.render(
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/firmy/1/faktury" element={<ProtectedRoute><div>Protected content</div></ProtectedRoute>} />
              <Route path="/login" element={<LoginProbe />} />
            </Routes>
          </MemoryRouter>
        </QueryClientProvider>,
      )
    })
    for (let tick = 0; tick < 5; tick++) await act(flushPromises)
  }

  it('sends an unauthenticated user to the Keycloak login once, with path, search and hash', async () => {
    const login = vi.fn().mockResolvedValue(undefined)

    await renderAt('/firmy/1/faktury?strona=2#f-9', { login })
    await act(async () => {
      await flushPromises()
    })

    expect(login).toHaveBeenCalledTimes(1)
    expect(login).toHaveBeenCalledWith('/firmy/1/faktury?strona=2#f-9')
    expect(container.textContent).toBe('Przekierowanie do logowania…')
  })

  it('does not redirect to Keycloak again after a final 401 ended the session', async () => {
    const login = vi.fn().mockResolvedValue(undefined)

    await renderAt('/firmy/1/faktury?strona=2', { login, sessionEnded: true })

    expect(login).not.toHaveBeenCalled()
    expect(container.textContent).toBe('login from=/firmy/1/faktury?strona=2')
  })

  it('shows the login page when the redirect to Keycloak fails', async () => {
    const login = vi.fn().mockRejectedValue(new Error('discovery failed'))

    await renderAt('/firmy/1/faktury', { login })
    await act(async () => {
      await flushPromises()
    })

    expect(container.textContent).toBe('login from=/firmy/1/faktury')
  })
})
