// @vitest-environment jsdom
import { act, useEffect } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from './AuthProvider'

describe('useAuth', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('throws when used outside AuthProvider', async () => {
    const { useAuth } = await import('./useAuth')

    function Consumer() {
      useAuth()
      return null
    }

    expect(() => {
      act(() => {
        root.render(<Consumer />)
      })
    }).toThrow('useAuth must be used within AuthProvider')
  })

  it('returns auth context when used inside AuthProvider', async () => {
    const { useAuth } = await import('./useAuth')

    const setIsAuthenticated = vi.fn<(value: boolean) => void>()
    function Consumer() {
      const auth = useAuth()
      useEffect(() => {
        setIsAuthenticated(auth.isAuthenticated)
      }, [auth.isAuthenticated])
      return null
    }

    await act(async () => {
      root.render(
        <AuthProvider>
          <Consumer />
        </AuthProvider>,
      )
      await new Promise((resolve) => setTimeout(resolve, 0))
    })

    expect(typeof setIsAuthenticated.mock.calls.at(-1)?.[0]).toBe('boolean')
  })
})
