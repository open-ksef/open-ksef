// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, useEffect } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useTenants } from './useTenants'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn().mockResolvedValue([{ id: 'tenant-1', nip: '1234567890', displayName: 'Tenant A', notificationEmail: null, createdAt: new Date().toISOString() }]),
}))

describe('query hooks', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('loads tenants with useTenants hook', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const setCount = vi.fn<(count: number) => void>()

    function Consumer() {
      const tenantsQuery = useTenants()
      useEffect(() => {
        if (tenantsQuery.data) {
          setCount(tenantsQuery.data.length)
        }
      }, [tenantsQuery.data])
      return null
    }

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <Consumer />
        </QueryClientProvider>,
      )
    })

    await act(async () => {
      const start = Date.now()
      while (Date.now() - start < 1000) {
        if (setCount.mock.calls.some((call) => call[0] === 1)) {
          return
        }
        await new Promise((resolve) => setTimeout(resolve, 0))
      }
    })

    expect(setCount).toHaveBeenCalledWith(1)
  })
})
