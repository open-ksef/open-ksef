// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { loadRoute } from '@/loadRoute'
import { RouteError } from './RouteError'

const chunkError = () => new TypeError('Failed to fetch dynamically imported module: http://localhost/assets/Settings-old.js')

describe('RouteError', () => {
  const reload = vi.fn()
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    sessionStorage.clear()
    reload.mockClear()
    vi.stubGlobal('location', { reload })
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
    vi.unstubAllGlobals()
  })

  async function navigate(load: () => Promise<unknown>, path = '/settings') {
    const router = createMemoryRouter([
      {
        errorElement: <RouteError />,
        children: [
          { path: '/', element: <h1>Start</h1> },
          { path, lazy: () => loadRoute(load).then(() => ({ Component: () => <h1>Ustawienia</h1> })) },
        ],
      },
    ])
    await act(async () => root.render(<RouterProvider router={router} />))
    await act(async () => {
      await router.navigate(path)
    })
    return router
  }

  it('reloads once at the target URL when a chunk fails to load', async () => {
    const router = await navigate(() => Promise.reject(chunkError()))

    expect(router.state.location.pathname).toBe('/settings')
    expect(reload).toHaveBeenCalledOnce()
    expect(sessionStorage.getItem('openksef:chunk-reload')).toBe('/settings')
  })

  it('shows the error screen when the same chunk fails again after the reload', async () => {
    sessionStorage.setItem('openksef:chunk-reload', '/settings')

    await navigate(() => Promise.reject(chunkError()))

    expect(reload).not.toHaveBeenCalled()
    expect(container.querySelector('[role="alert"] h1')?.textContent).toBe('Nie udało się wyświetlić strony')
  })

  it('reloads again for a later chunk failure on another page', async () => {
    sessionStorage.setItem('openksef:chunk-reload', '/settings')

    await navigate(() => Promise.reject(chunkError()), '/mobile-setup')

    expect(reload).toHaveBeenCalledOnce()
  })

  it('clears the guard after a successful lazy load', async () => {
    sessionStorage.setItem('openksef:chunk-reload', '/settings')

    await navigate(() => Promise.resolve({}))

    expect(container.querySelector('h1')?.textContent).toBe('Ustawienia')
    expect(sessionStorage.getItem('openksef:chunk-reload')).toBeNull()
  })

  it('does not reload for other errors', async () => {
    await navigate(() => Promise.reject(new Error('boom')))

    expect(reload).not.toHaveBeenCalled()
    expect(container.querySelector('[role="alert"]')).not.toBeNull()
  })
})
