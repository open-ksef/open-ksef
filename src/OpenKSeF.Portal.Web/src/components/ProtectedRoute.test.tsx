// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuth } from '@/auth/useAuth'
import { ProtectedRoute } from './ProtectedRoute'

vi.mock('@/auth/useAuth', () => ({
  useAuth: vi.fn(),
}))

describe('ProtectedRoute', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
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

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/']}>
          <Routes>
            <Route path="/" element={<ProtectedRoute><div>Protected content</div></ProtectedRoute>} />
            <Route path="/login" element={<div>Login page</div>} />
          </Routes>
        </MemoryRouter>,
      )
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

    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={['/']}>
          <Routes>
            <Route path="/" element={<ProtectedRoute><div>Protected content</div></ProtectedRoute>} />
            <Route path="/login" element={<div>Login page</div>} />
          </Routes>
        </MemoryRouter>,
      )
    })

    expect(container.textContent).toContain('Login page')
    expect(container.textContent).not.toContain('Protected content')
  })
})
