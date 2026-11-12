// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { loadRoute } from './loadRoute'

describe('loadRoute', () => {
  const reload = vi.fn()

  beforeEach(() => {
    sessionStorage.clear()
    reload.mockClear()
    vi.stubGlobal('location', { reload })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reloads once when a chunk fails, then shows the error on the second failure', async () => {
    const failing = () => Promise.reject(new TypeError('Failed to fetch dynamically imported module'))

    let settled = false
    void loadRoute(failing).finally(() => (settled = true))
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(reload).toHaveBeenCalledOnce()
    expect(settled).toBe(false)

    await expect(loadRoute(failing)).rejects.toThrow('Failed to fetch dynamically imported module')
    expect(reload).toHaveBeenCalledOnce()
  })

  it('clears the guard after a successful load so a later deploy can reload again', async () => {
    sessionStorage.setItem('openksef:chunk-reload', '1')

    await expect(loadRoute(() => Promise.resolve('page'))).resolves.toBe('page')

    void loadRoute(() => Promise.reject(new Error('chunk')))
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(reload).toHaveBeenCalledOnce()
  })
})
