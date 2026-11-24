// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createDraftFromTemplate, deleteTemplate, listTemplates, renameTemplate } from '@/api/endpoints/issuedInvoices'
import { listTenants } from '@/api/endpoints/tenants'
import type { InvoiceTemplateResponse } from '@/api/types'
import { CompanyProvider } from '@/company/CompanyProvider'
import { memoryStorage } from '@/company/memoryStorage'
import { ownerAccess } from '@/test/tenant'
import { InvoiceTemplatesPage } from './InvoiceTemplates'

vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({
  listTemplates: vi.fn(),
  renameTemplate: vi.fn(),
  deleteTemplate: vi.fn(),
  createDraftFromTemplate: vi.fn(),
}))

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))

const template: InvoiceTemplateResponse = {
  id: 'tpl-1',
  name: 'Abonament X',
  buyer: { kind: 'Company', nip: '5260250274', name: 'Firma Testowa', addressLine1: 'ul. A 1', addressLine2: '00-001 Warszawa' },
  currency: 'PLN',
  paymentDays: 21,
  lineCount: 2,
  createdAt: '2026-11-24T10:00:00Z',
}

describe('Szablony', () => {
  let root: Root
  let container: HTMLDivElement
  let router: ReturnType<typeof createMemoryRouter>

  async function render(): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    router = createMemoryRouter(
      [
        { path: '/issued/:id/edit', element: <p>Formularz szkicu</p> },
        {
          path: '*',
          element: (
            <QueryClientProvider client={client}>
              <CompanyProvider>
                <InvoiceTemplatesPage />
              </CompanyProvider>
            </QueryClientProvider>
          ),
        },
      ],
      { initialEntries: ['/issued/templates?tenantId=a'] },
    )
    await act(async () => root.render(<RouterProvider router={router} />))
    await settle()
  }

  async function settle(): Promise<void> {
    for (let tick = 0; tick < 8; tick++) await act(flush)
  }

  const button = (text: string) => [...container.querySelectorAll('button')].find((item) => item.textContent?.includes(text))
  const byLabel = (label: string) => container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`)!
  const confirm = () => container.querySelector<HTMLButtonElement>('.ui-modal-footer .ui-button--primary, .ui-modal-footer .ui-button--danger')!

  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubGlobal('localStorage', memoryStorage())
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(listTenants).mockResolvedValue([{ id: 'a', nip: '1111111111', displayName: 'Firma A', notificationEmail: null, ...ownerAccess, createdAt: '2026-01-01T00:00:00Z' }])
    vi.mocked(listTemplates).mockResolvedValue([template])
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('lists templates with buyer, line count, currency and term', async () => {
    await render()
    const row = container.querySelector('[data-testid="template-table"] tbody tr')?.textContent
    expect(row).toContain('Abonament X')
    expect(row).toContain('Firma Testowa · NIP 5260250274')
    expect(row).toContain('2 pozycje · PLN · termin 21 dni')
  })

  it('Nowa z szablonu creates a draft and opens its edit form', async () => {
    vi.mocked(createDraftFromTemplate).mockResolvedValue({ id: 'new-1' } as never)
    await render()
    await act(async () => byLabel('Nowa z szablonu Abonament X').click())
    await settle()
    expect(createDraftFromTemplate).toHaveBeenCalledWith('a', 'tpl-1')
    expect(router.state.location.pathname).toBe('/issued/new-1/edit')
  })

  it('renames a template and refreshes the list', async () => {
    vi.mocked(renameTemplate).mockResolvedValue({ ...template, name: 'Abonament Y' })
    await render()
    await act(async () => byLabel('Zmień nazwę szablonu Abonament X').click())
    const input = container.querySelector<HTMLInputElement>('#template-name')!
    expect(input.value).toBe('Abonament X')
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, 'Abonament Y')
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })
    await act(async () => confirm().click())
    await settle()
    expect(renameTemplate).toHaveBeenCalledWith('a', 'tpl-1', 'Abonament Y')
    expect(listTemplates).toHaveBeenCalledTimes(2)
  })

  it('deletes a template only after confirmation', async () => {
    vi.mocked(deleteTemplate).mockResolvedValue(undefined)
    await render()
    await act(async () => byLabel('Usuń szablon Abonament X').click())
    expect(deleteTemplate).not.toHaveBeenCalled()
    expect(container.textContent).toContain('Szablon „Abonament X” zostanie usunięty.')
    await act(async () => confirm().click())
    await settle()
    expect(deleteTemplate).toHaveBeenCalledWith('a', 'tpl-1')
    expect(container.querySelector('.ui-modal')).toBeNull()
  })

  it('an empty list explains where templates come from', async () => {
    vi.mocked(listTemplates).mockResolvedValue([])
    await render()
    expect(container.textContent).toContain('Nie masz jeszcze szablonów')
    expect(button('Nowa z szablonu')).toBeUndefined()
  })
})
