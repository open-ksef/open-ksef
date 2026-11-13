// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LoginPage } from './Login'

const auth = vi.hoisted(() => ({
  isAuthenticated: false,
  isLoading: false,
  login: vi.fn(),
}))

vi.mock('@/auth/useAuth', () => ({ useAuth: () => auth }))

describe('LoginPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(from?: string): Promise<void> {
    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={[{ pathname: '/login', state: from ? { from } : null }]}>
          <LoginPage />
        </MemoryRouter>,
      )
    })
  }

  it('has one login button, no credential form and no emoji', async () => {
    await render()

    const logo = container.querySelector('.auth-logo')
    expect(logo?.querySelector('svg path')?.getAttribute('fill')).toBe('currentColor')
    expect(container.querySelectorAll('button')).toHaveLength(1)
    expect(container.querySelector('input, form')).toBeNull()
    expect(container.textContent).not.toMatch(/\p{Extended_Pictographic}/u)
  })

  it('sends the user to Keycloak with the route it came from', async () => {
    auth.login.mockResolvedValue(undefined)
    await render('/telefon?x=1')

    await act(async () => container.querySelector<HTMLButtonElement>('[data-testid="login-submit"]')!.click())

    expect(auth.login).toHaveBeenCalledWith('/telefon?x=1')
  })

  it.each(['//evil.com', 'https://evil.com'])('ignores an off-site return path %s', async (from) => {
    auth.login.mockResolvedValue(undefined)
    await render(from)

    await act(async () => container.querySelector<HTMLButtonElement>('[data-testid="login-submit"]')!.click())

    expect(auth.login).toHaveBeenCalledWith('/')
  })

  it('shows an error and enables the button again when Keycloak cannot be reached', async () => {
    auth.login.mockRejectedValue(new Error('Failed to fetch'))
    await render()

    await act(async () => container.querySelector<HTMLButtonElement>('[data-testid="login-submit"]')!.click())

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Nie udało się otworzyć strony logowania')
    expect(container.querySelector<HTMLButtonElement>('[data-testid="login-submit"]')!.disabled).toBe(false)
  })
})
