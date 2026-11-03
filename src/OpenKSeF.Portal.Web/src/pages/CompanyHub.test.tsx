// @vitest-environment jsdom
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { expect, it } from 'vitest'

import { CompanyHubPage } from './CompanyHub'

it('links to the three company screens under their current names', async () => {
  ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
  const container = document.createElement('div')
  const root = createRoot(container)
  await act(async () =>
    root.render(
      <MemoryRouter>
        <CompanyHubPage />
      </MemoryRouter>,
    ),
  )

  const link = (id: string) => container.querySelector(`[data-testid="company-hub-link-${id}"]`)
  expect(container.querySelector('h1')?.textContent).toBe('Firma')
  expect(link('data')?.getAttribute('href')).toBe('/seller-profile')
  expect(link('data')?.textContent).toBe('Dane do faktur')
  expect(link('credentials')?.getAttribute('href')).toBe('/credentials')
  expect(link('credentials')?.textContent).toBe('Dane logowania')
  expect(link('tenants')?.getAttribute('href')).toBe('/tenants')
  expect(link('tenants')?.textContent).toBe('Firmy')
  act(() => root.unmount())
})
