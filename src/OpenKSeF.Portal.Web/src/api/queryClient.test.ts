import { describe, expect, it, vi } from 'vitest'

import { queryClient } from './queryClient'

describe('queryClient', () => {
  it('uses expected default query options', () => {
    const defaults = queryClient.getDefaultOptions().queries

    expect(defaults?.retry).toBe(1)
    expect(defaults?.refetchOnWindowFocus).toBe(false)
    expect(defaults?.staleTime).toBe(30000)
  })

  it('logs query errors through global handler', async () => {
    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    const cache = queryClient.getQueryCache()
    cache.config.onError?.(new Error('boom'), {} as never)

    expect(consoleSpy).toHaveBeenCalled()
    consoleSpy.mockRestore()
  })
})
