// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LoginPage } from './Login'

const auth = vi.hoisted(() => ({
  isAuthenticated: false,
  isLoading: false,
  sessionEnded: true,
  login: vi.fn(),
}))

vi.mock('@/auth/useAuth', () => ({ useAuth: () => auth }))

describe('LoginPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    auth.sessionEnded = true
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(from?: string, search = ''): Promise<void> {
    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={[{ pathname: '/login', search, state: from ? { from } : null }]}>
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

  it('redirects to Keycloak at once with the route it came from', async () => {
    auth.sessionEnded = false
    auth.login.mockResolvedValue(undefined)

    await render('/telefon?x=1')

    expect(auth.login).toHaveBeenCalledTimes(1)
    expect(auth.login).toHaveBeenCalledWith('/telefon?x=1')
    expect(container.querySelector<HTMLButtonElement>('[data-testid="login-submit"]')!.disabled).toBe(true)
  })

  it('takes the return path from the query when there is no router state', async () => {
    auth.sessionEnded = false
    auth.login.mockResolvedValue(undefined)

    await render(undefined, '?returnTo=%2Fsettings%2Fsecurity')

    expect(auth.login).toHaveBeenCalledWith('/settings/security')
  })

  it('stays on the page after the session ended and logs in on click', async () => {
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
