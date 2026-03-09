// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { addOrUpdateCredential, deleteCredential, forceCredentialSync, listCredentials } from '@/api/endpoints/credentials'
import { listTenants } from '@/api/endpoints/tenants'
import { CredentialListPage } from './CredentialList'

vi.mock('@/api/endpoints/credentials', () => ({
  listCredentials: vi.fn(),
  addOrUpdateCredential: vi.fn(),
  deleteCredential: vi.fn(),
  forceCredentialSync: vi.fn(),
}))

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

function flushPromises() {
  return new Promise<void>((resolve) => setTimeout(resolve, 0))
}

function setTextareaValue(element: HTMLTextAreaElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set
  setter?.call(element, value)
}

function setSelectValue(element: HTMLSelectElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set
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

describe('CredentialListPage', () => {
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
        notificationEmail: null,
        createdAt: '2026-01-01T00:00:00Z',
      },
    ])

    vi.mocked(listCredentials).mockResolvedValue([
      {
        tenantId: 'tenant-1',
        tenantDisplayName: 'Tenant A',
        hasCredential: true,
        credentialType: 'Token',
        lastUpdatedAt: '2026-02-01T00:00:00Z',
      },
    ])

    vi.mocked(addOrUpdateCredential).mockResolvedValue({ message: 'ok' })
    vi.mocked(deleteCredential).mockResolvedValue()
    vi.mocked(forceCredentialSync).mockResolvedValue({
      tenantId: 'tenant-1',
      fetchedInvoices: 3,
      newInvoices: 2,
      syncedAtUtc: '2026-02-28T22:30:00Z',
    })

    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders credential table with actions', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <CredentialListPage />
        </QueryClientProvider>,
      )
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => (container.textContent ?? '').includes('Tenant A'))
    })

    expect(container.textContent).toContain('Skonfigurowany')
    expect(container.querySelector('[data-testid="credential-add-button"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="credential-refresh-button"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="credential-sync-button"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="credential-update-button"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="credential-delete-button"]')).toBeTruthy()
  })

  it('submits add/update credential mutation', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <CredentialListPage />
        </QueryClientProvider>,
      )
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="credential-add-button"]') !== null)
    })

    await act(async () => {
      ;(container.querySelector('[data-testid="credential-add-button"]') as HTMLButtonElement).click()
    })

    const tenantSelect = container.querySelector('[data-testid="credential-tenant-select"]') as HTMLSelectElement
    const tokenInput = container.querySelector('[data-testid="credential-token-input"]') as HTMLTextAreaElement

    await act(async () => {
      setSelectValue(tenantSelect, 'tenant-1')
      tenantSelect.dispatchEvent(new Event('change', { bubbles: true }))
      setTextareaValue(tokenInput, 'secret-token')
      tokenInput.dispatchEvent(new Event('input', { bubbles: true }))
      ;(container.querySelector('[data-testid="credential-submit-button"]') as HTMLButtonElement).click()
      await flushPromises()
      await flushPromises()
    })

    expect(vi.mocked(addOrUpdateCredential).mock.calls[0]?.[0]).toBe('tenant-1')
    expect(vi.mocked(addOrUpdateCredential).mock.calls[0]?.[1]).toBe('secret-token')
  })

  it('runs force sync mutation for selected tenant', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <QueryClientProvider client={client}>
          <CredentialListPage />
        </QueryClientProvider>,
      )
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => container.querySelector('[data-testid="credential-sync-button"]') !== null)
      ;(container.querySelector('[data-testid="credential-sync-button"]') as HTMLButtonElement).click()
      await flushPromises()
      await flushPromises()
    })

    expect(vi.mocked(forceCredentialSync).mock.calls[0]?.[0]).toBe('tenant-1')
  })
})
