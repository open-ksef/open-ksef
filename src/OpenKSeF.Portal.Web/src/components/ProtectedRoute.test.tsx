// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
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
    vi.mocked(getOnboardingStatus).mockResolvedValue({ isComplete: true })

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
      loginWithCredentials: vi.fn(),
      loginWithGoogle: vi.fn(),
      register: vi.fn(),
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

  it('redirects unauthenticated users to login route', async () => {
    vi.mocked(useAuth).mockReturnValue({
      user: null,
      isLoading: false,
      isAuthenticated: false,
      login: vi.fn(),
      loginWithCredentials: vi.fn(),
      loginWithGoogle: vi.fn(),
      register: vi.fn(),
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

    expect(container.textContent).toContain('Login page')
    expect(container.textContent).not.toContain('Protected content')
  })
})
