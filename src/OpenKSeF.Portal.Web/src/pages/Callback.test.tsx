// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const mockNavigate = vi.fn()
const mockHandleOidcCallback = vi.fn<() => Promise<void>>()

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
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('processes callback and navigates to home on success', async () => {
    mockHandleOidcCallback.mockResolvedValue(undefined)

    const { Callback } = await import('./Callback')

    await act(async () => {
      root.render(<Callback />)
      await new Promise((resolve) => setTimeout(resolve, 0))
    })

    expect(mockHandleOidcCallback).toHaveBeenCalledTimes(1)
    expect(mockNavigate).toHaveBeenCalledWith('/', { replace: true })
  })

  it('renders error state on callback failure', async () => {
    mockHandleOidcCallback.mockRejectedValue(new Error('invalid_grant'))

    const { Callback } = await import('./Callback')

    await act(async () => {
      root.render(<Callback />)
      await new Promise((resolve) => setTimeout(resolve, 0))
    })

    expect(container.textContent).toContain('Authentication failed')
  })
})
