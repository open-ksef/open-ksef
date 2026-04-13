// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it } from 'vitest'

import { InvoiceListPage } from './InvoiceList'

function flushPromises() {
  return new Promise<void>((resolve) => setTimeout(resolve, 0))
}

describe('InvoiceListPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('redirects /invoices to /invoices/sales', async () => {
    const router = createMemoryRouter(
      [
        { path: '/invoices', element: <InvoiceListPage /> },
        { path: '/invoices/sales', element: <div data-testid="sales-page">Faktury sprzedaży</div> },
      ],
      { initialEntries: ['/invoices'] },
    )

    await act(async () => {
      root.render(<RouterProvider router={router} />)
      await flushPromises()
    })

    expect(router.state.location.pathname).toBe('/invoices/sales')
    expect(container.querySelector('[data-testid="sales-page"]')).toBeTruthy()
  })
})
