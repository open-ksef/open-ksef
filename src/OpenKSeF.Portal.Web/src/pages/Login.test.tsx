// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LoginPage } from './Login'

const auth = vi.hoisted(() => ({
  isAuthenticated: false,
  isLoading: false,
  loginWithCredentials: vi.fn(),
  loginWithGoogle: vi.fn(),
  register: vi.fn(),
}))

vi.mock('@/auth/useAuth', () => ({ useAuth: () => auth }))

const setupStatus = vi.hoisted(() => ({ data: undefined as { isInitialized: boolean; registrationAllowed?: boolean } | undefined }))

vi.mock('@/hooks/useSetupStatus', () => ({ useSetupStatus: () => setupStatus }))

describe('LoginPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('shows the brand mark as an SVG and no emoji', async () => {
    await act(async () => {
      root.render(
        <MemoryRouter>
          <LoginPage />
        </MemoryRouter>,
      )
    })

    const logo = container.querySelector('.auth-logo')
    expect(logo?.textContent).toBe('')
    expect(logo?.querySelector('svg path')?.getAttribute('fill')).toBe('currentColor')
    expect(container.textContent).not.toMatch(/\p{Extended_Pictographic}/u)
  })
  it.each([
    [undefined, true],
    [{ isInitialized: true }, true],
    [{ isInitialized: true, registrationAllowed: true }, true],
    [{ isInitialized: true, registrationAllowed: false }, false],
  ])('register link for setup status %j shown=%s', async (data, shown) => {
    setupStatus.data = data
    await act(async () => {
      root.render(
        <MemoryRouter>
          <LoginPage />
        </MemoryRouter>,
      )
    })

    expect(container.querySelector('[data-testid="switch-to-register"]') !== null).toBe(shown)
    expect(container.textContent?.includes('Nie masz konta?')).toBe(shown)
  })

  it('after form login goes to the route it came from, and Google gets it too', async () => {
    auth.loginWithCredentials.mockResolvedValue(undefined)
    function Where() {
      return <div>at={useLocation().pathname + useLocation().search}</div>
    }
    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={[{ pathname: '/login', state: { from: '/telefon?x=1' } }]}>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="*" element={<Where />} />
          </Routes>
        </MemoryRouter>,
      )
    })

    act(() => container.querySelector<HTMLButtonElement>('[data-testid="login-google"]')!.click())
    expect(auth.loginWithGoogle).toHaveBeenCalledWith('/telefon?x=1')

    const type = (id: string, value: string) => {
      const input = container.querySelector<HTMLInputElement>(`[data-testid="${id}"]`)!
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value)
      input.dispatchEvent(new Event('input', { bubbles: true }))
    }
    act(() => {
      type('login-email', 'jan@firma.pl')
      type('login-password', 'Haslo1234!')
    })

    await act(async () => {
      container.querySelector('[data-testid="login-submit"]')!.closest('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    })
    expect(auth.loginWithCredentials).toHaveBeenCalledWith('jan@firma.pl', 'Haslo1234!')
    expect(container.textContent).toBe('at=/telefon?x=1')
  })

  it.each(['//evil.com', 'https://evil.com'])('ignores an off-site return path %s', async (from) => {
    auth.loginWithGoogle.mockClear()
    await act(async () => {
      root.render(
        <MemoryRouter initialEntries={[{ pathname: '/login', state: { from } }]}>
          <LoginPage />
        </MemoryRouter>,
      )
    })

    act(() => container.querySelector<HTMLButtonElement>('[data-testid="login-google"]')!.click())
    expect(auth.loginWithGoogle).toHaveBeenCalledWith('/')
  })
})
