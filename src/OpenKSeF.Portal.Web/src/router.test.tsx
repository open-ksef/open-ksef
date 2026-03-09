// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuth } from '@/auth/useAuth'
import { appRoutes } from './router'

vi.mock('@/auth/useAuth', () => ({
  useAuth: vi.fn(),
}))

describe('router', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders login route without authentication', async () => {
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

    const router = createMemoryRouter(appRoutes, { initialEntries: ['/login'] })

    await act(async () => {
      root.render(<RouterProvider router={router} />)
    })

    expect(container.textContent).toContain('Sign in to OpenKSeF Portal')
  })

  it('redirects protected route to login when unauthenticated', async () => {
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

    const router = createMemoryRouter(appRoutes, { initialEntries: ['/tenants'] })

    await act(async () => {
      root.render(<RouterProvider router={router} />)
    })

    expect(container.textContent).toContain('Sign in to OpenKSeF Portal')
  })
})
