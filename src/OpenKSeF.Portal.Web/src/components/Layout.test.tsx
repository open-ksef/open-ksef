// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { Layout } from './Layout'

vi.mock('@/auth/useAuth', () => ({
  useAuth: vi.fn().mockReturnValue({
    user: { profile: { name: 'Portal User' } },
    isLoading: false,
    isAuthenticated: true,
    login: vi.fn(),
    loginWithCredentials: vi.fn(),
    loginWithGoogle: vi.fn(),
    register: vi.fn(),
    logout: vi.fn(),
    getAccessToken: vi.fn(),
  }),
}))

describe('Layout', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('toggles mobile nav visibility using hamburger button', async () => {
    await act(async () => {
      root.render(
        <MemoryRouter>
          <Layout />
        </MemoryRouter>,
      )
    })

    const nav = container.querySelector('[data-testid="mobile-nav"]')
    expect(nav?.getAttribute('data-open')).toBe('false')

    const button = container.querySelector('[data-testid="mobile-nav-toggle"]') as HTMLButtonElement

    await act(async () => {
      button.click()
    })

    expect(nav?.getAttribute('data-open')).toBe('true')
  })
})
