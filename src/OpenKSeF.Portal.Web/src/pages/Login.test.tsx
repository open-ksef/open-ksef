// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LoginPage } from './Login'

vi.mock('@/auth/useAuth', () => ({
  useAuth: () => ({
    isAuthenticated: false,
    isLoading: false,
    loginWithCredentials: vi.fn(),
    loginWithGoogle: vi.fn(),
    register: vi.fn(),
  }),
}))

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
})
