// @vitest-environment jsdom
import { act, StrictMode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockNavigate = vi.fn()
const mockHandleOidcCallback = vi.fn<() => Promise<string>>()

vi.mock('react-router-dom', () => ({
  useNavigate: () => mockNavigate,
}))

vi.mock('@/auth/useAuth', () => ({
  useAuth: () => ({
    handleOidcCallback: mockHandleOidcCallback,
  }),
}))

describe('Callback page', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    window.history.replaceState({}, '', '/callback?code=abc&state=xyz')
    sessionStorage.clear()
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('exchanges the code once under StrictMode and lands on the requested route', async () => {
    mockHandleOidcCallback.mockResolvedValue('/firmy/1/faktury?strona=2')

    const { Callback } = await import('./Callback')

    await act(async () => {
      root.render(<StrictMode><Callback /></StrictMode>)
      await new Promise((resolve) => setTimeout(resolve, 0))
    })

    expect(mockHandleOidcCallback).toHaveBeenCalledTimes(1)
    expect(mockNavigate).toHaveBeenCalledTimes(1)
    expect(mockNavigate).toHaveBeenCalledWith('/firmy/1/faktury?strona=2', { replace: true })
  })

  async function renderCallback(): Promise<void> {
    const { Callback } = await import('./Callback')
    await act(async () => {
      root.render(<StrictMode><Callback /></StrictMode>)
      await new Promise((resolve) => setTimeout(resolve, 0))
    })
  }

  it('shows a Polish error, never the raw one', async () => {
    mockHandleOidcCallback.mockRejectedValue(new Error('Network Error'))

    await renderCallback()

    expect(container.textContent).toContain('Logowanie nie powiodło się')
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Nie udało się dokończyć logowania. Zaloguj się ponownie.')
  })

  it('starts a fresh login once on a stale code, then shows the Polish error', async () => {
    mockHandleOidcCallback.mockRejectedValue(Object.assign(new Error('Code not valid'), { error: 'invalid_grant' }))

    await renderCallback()
    expect(mockNavigate).toHaveBeenCalledWith('/login', { replace: true })
    expect(container.textContent).not.toContain('Code not valid')

    act(() => root.unmount())
    root = createRoot(container)
    mockNavigate.mockClear()
    await renderCallback()
    expect(mockNavigate).not.toHaveBeenCalled()
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Nie udało się dokończyć logowania. Zaloguj się ponownie.')
  })
})
