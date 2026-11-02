// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import toast from 'react-hot-toast'

import { listIssuedInvoices } from '@/api/endpoints/issuedInvoices'
import { createTenant, deleteTenant, listTenants, updateTenant } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import { TenantListPage } from './TenantList'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
  createTenant: vi.fn(),
  updateTenant: vi.fn(),
  deleteTenant: vi.fn(),
}))

vi.mock('@/api/endpoints/issuedInvoices', () => ({
  listIssuedInvoices: vi.fn(),
}))

function flushPromises() {
  return new Promise<void>((resolve) => setTimeout(resolve, 0))
}

function setInputValue(element: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  setter?.call(element, value)
}

async function waitFor(assertion: () => boolean, timeoutMs = 1000): Promise<void> {
  const start = Date.now()
  while (Date.now() - start < timeoutMs) {
    if (assertion()) return
    await flushPromises()
  }

  throw new Error('Condition not met within timeout')
}

async function settle(assertion: () => boolean): Promise<void> {
  for (let i = 0; i < 100 && !assertion(); i++) {
    await act(flushPromises)
  }
  expect(assertion()).toBe(true)
}

describe('TenantListPage', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(listTenants).mockResolvedValue([
      {
        id: 'tenant-1',
        nip: '1234567890',
        displayName: 'Tenant A',
        notificationEmail: 'a@example.com',
        createdAt: '2026-01-01T00:00:00Z',
      },
    ])

    vi.mocked(createTenant).mockResolvedValue({
      id: 'tenant-2',
      nip: '0987654321',
      displayName: 'Tenant B',
      notificationEmail: 'b@example.com',
      createdAt: '2026-02-01T00:00:00Z',
    })

    vi.mocked(updateTenant).mockResolvedValue({
      id: 'tenant-1',
      nip: '1234567890',
      displayName: 'Tenant Updated',
      notificationEmail: 'updated@example.com',
      createdAt: '2026-01-01T00:00:00Z',
    })

    vi.mocked(deleteTenant).mockResolvedValue()
    vi.mocked(listIssuedInvoices).mockResolvedValue({ items: [], page: 1, pageSize: 1, totalCount: 3, totalPages: 3 })

    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  async function openDeleteDialog(): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <TenantListPage />
        </QueryClientProvider>,
      )
      await flushPromises()
    })
    await settle(() => (container.textContent ?? '').includes('Tenant A'))

    await act(async () => {
      container.querySelector<HTMLButtonElement>('[data-testid="tenant-delete-button"]')!.click()
    })
  }

  const notice = () => container.querySelector('[data-testid="tenant-delete-invoices"]')?.textContent ?? ''
  const confirmButton = () =>
    [...container.querySelectorAll<HTMLButtonElement>('[role="alertdialog"] button')].find((b) => b.textContent === 'Usuń firmę')!

  it('renders tenant table and actions', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <TenantListPage />
        </QueryClientProvider>,
      )
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => (container.textContent ?? '').includes('Tenant A'))
    })

    expect(container.textContent).toContain('1234567890')
    expect(container.textContent).toContain('a@example.com')
    expect(container.querySelector('[data-testid="tenant-create-button"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="tenant-refresh-button"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="tenant-edit-button"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="tenant-delete-button"]')).toBeTruthy()
  })

  it('names the issued invoice count and that they cannot be restored', async () => {
    await openDeleteDialog()
    await settle(() => notice().includes('liczba: 3'))

    expect(vi.mocked(listIssuedInvoices)).toHaveBeenCalledWith('tenant-1', { pageSize: 1 })
    expect(notice()).toContain('Nie będzie można ich przywrócić')
    expect(notice()).toContain('Firmy nie usuniemy, jeśli')
    expect(container.querySelector('[role="alertdialog"]')?.textContent).toContain('pobrane faktury zakupowe')
  })

  it('names the seller profile, paid marks and numbering restart as lost', async () => {
    vi.mocked(listIssuedInvoices).mockReturnValue(new Promise(() => {}))
    await openDeleteDialog()

    const dialog = container.querySelector('[role="alertdialog"]')?.textContent
    expect(dialog).toContain('pobrane faktury zakupowe z oznaczeniami opłacenia')
    expect(dialog).toContain('profil sprzedawcy z rachunkiem bankowym')
    expect(dialog).toContain('Po ponownym dodaniu firmy numeracja zacznie się od 1.')
  })

  it('warns while the count loads without blocking confirm', async () => {
    vi.mocked(listIssuedInvoices).mockReturnValue(new Promise(() => {}))
    await openDeleteDialog()

    expect(notice()).toContain('Sprawdzamy liczbę')
    expect(notice()).toContain('nie będzie można ich przywrócić')
    expect(confirmButton().disabled).toBe(false)
  })

  it('says there is nothing to lose when the count is zero', async () => {
    vi.mocked(listIssuedInvoices).mockResolvedValue({ items: [], page: 1, pageSize: 1, totalCount: 0, totalPages: 0 })
    await openDeleteDialog()
    await settle(() => notice().includes('nie ma wystawionych faktur'))

    expect(notice()).not.toContain('przywrócić')
  })

  it('states the unknown count and still allows confirm when the count fails', async () => {
    vi.mocked(listIssuedInvoices).mockRejectedValue(new Error('boom'))
    await openDeleteDialog()
    await settle(() => notice().includes('Nie udało się sprawdzić'))

    expect(notice()).toContain('Jeśli firma je ma')
    expect(notice()).toContain('nie będzie można ich przywrócić')
    expect(confirmButton().disabled).toBe(false)

    await act(async () => confirmButton().click())
    await settle(() => vi.mocked(deleteTenant).mock.calls.length > 0)
  })

  it('reports a tenant already gone and refreshes the list on 404', async () => {
    const toastError = vi.spyOn(toast, 'error')
    vi.mocked(deleteTenant).mockRejectedValue(await normalizeError(new Response(null, { status: 404 })))
    await openDeleteDialog()
    const listCalls = vi.mocked(listTenants).mock.calls.length

    await act(async () => confirmButton().click())
    await settle(() => toastError.mock.calls.length > 0)

    expect(String(toastError.mock.calls[0][0])).toContain('Firma już nie istnieje.')
    await settle(() => vi.mocked(listTenants).mock.calls.length > listCalls)
    toastError.mockRestore()
  })

  it('shows the API message as is when tenant delete returns 409', async () => {
    const message = 'Nie można usunąć firmy w trakcie wysyłki faktur do KSeF. Poczekaj na wynik wysyłki i spróbuj ponownie.'
    const toastError = vi.spyOn(toast, 'error')
    vi.mocked(deleteTenant).mockRejectedValue(
      await normalizeError(new Response(JSON.stringify({ message }), { status: 409, headers: { 'Content-Type': 'application/json' } })),
    )
    await openDeleteDialog()

    await act(async () => confirmButton().click())
    await settle(() => toastError.mock.calls.length > 0)

    expect(toastError.mock.calls[0][0]).toBe(message)
    toastError.mockRestore()
  })

  it('validates and submits create form', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <TenantListPage />
        </QueryClientProvider>,
      )
      await flushPromises()
      await flushPromises()
    })

    const createButton = container.querySelector('[data-testid="tenant-create-button"]') as HTMLButtonElement
    await act(async () => {
      createButton.click()
    })

    const nipInput = container.querySelector('[data-testid="tenant-form-nip"]') as HTMLInputElement
    const nameInput = container.querySelector('[data-testid="tenant-form-display-name"]') as HTMLInputElement

    await act(async () => {
      setInputValue(nipInput, 'invalid')
      nipInput.dispatchEvent(new Event('input', { bubbles: true }))
      setInputValue(nameInput, 'Tenant B')
      nameInput.dispatchEvent(new Event('input', { bubbles: true }))
      ;(container.querySelector('[data-testid="tenant-form-submit"]') as HTMLButtonElement).click()
      await flushPromises()
    })

    expect(container.textContent).toContain('NIP musi zawierać dokładnie 10 cyfr')

    await act(async () => {
      setInputValue(nipInput, '0987654321')
      nipInput.dispatchEvent(new Event('input', { bubbles: true }))
      ;(container.querySelector('[data-testid="tenant-form-submit"]') as HTMLButtonElement).click()
      await flushPromises()
      await flushPromises()
    })

    expect(vi.mocked(createTenant).mock.calls[0]?.[0]).toEqual({
      nip: '0987654321',
      displayName: 'Tenant B',
      notificationEmail: null,
    })
  })
})
