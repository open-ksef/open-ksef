// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { createTenant, deleteTenant, listTenants, updateTenant } from '@/api/endpoints/tenants'
import { TenantListPage } from './TenantList'

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
  createTenant: vi.fn(),
  updateTenant: vi.fn(),
  deleteTenant: vi.fn(),
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

    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

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
