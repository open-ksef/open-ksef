// @vitest-environment jsdom
import axe from 'axe-core'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { Layout } from '@/components/Layout'

vi.mock('@/auth/useAuth', () => ({
  useAuth: vi.fn().mockReturnValue({
    user: { profile: { name: 'Portal User' } },
    isLoading: false,
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
    getAccessToken: vi.fn(),
  }),
}))

describe('accessibility checks', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('has no critical axe violations for layout shell', async () => {
    await act(async () => {
      root.render(
        <MemoryRouter>
          <Layout />
        </MemoryRouter>,
      )
    })

    const results = await axe.run(container, {
      rules: {
        'color-contrast': { enabled: false },
      },
    })

    const critical = results.violations.filter((violation) => violation.impact === 'critical')
    expect(critical).toHaveLength(0)
  })
})
