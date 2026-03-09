// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listTenants } from '@/api/endpoints/tenants'
import { listDevices, registerDevice, unregisterDevice } from '@/api/endpoints/devices'
import { DeviceListPage } from './DeviceList'

vi.mock('@/api/endpoints/devices', () => ({
  listDevices: vi.fn(),
  registerDevice: vi.fn(),
  unregisterDevice: vi.fn(),
}))

vi.mock('@/api/endpoints/tenants', () => ({
  listTenants: vi.fn(),
}))

function flushPromises() {
  return new Promise<void>((resolve) => setTimeout(resolve, 0))
}

async function waitFor(assertion: () => boolean, timeoutMs = 1000): Promise<void> {
  const start = Date.now()
  while (Date.now() - start < timeoutMs) {
    if (assertion()) return
    await flushPromises()
  }

  throw new Error('Condition not met within timeout')
}

describe('DeviceListPage', () => {
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

    vi.mocked(listDevices).mockResolvedValue([
      {
        id: 'device-1',
        token: 'abcdefghijklmnopqrstuvwxyz1234567890',
        platform: 0,
        tenantId: 'tenant-1',
        createdAt: '2026-02-01T00:00:00Z',
        updatedAt: '2026-02-02T00:00:00Z',
      },
    ])

    vi.mocked(registerDevice).mockResolvedValue({ message: 'registered' })
    vi.mocked(unregisterDevice).mockResolvedValue()

    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  it('renders devices table and actions', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <MemoryRouter>
          <QueryClientProvider client={client}>
            <DeviceListPage />
          </QueryClientProvider>
        </MemoryRouter>,
      )
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      await waitFor(() => (container.textContent ?? '').includes('Android'))
    })

    expect(container.querySelector('[data-testid="device-refresh-button"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="device-register-button"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="device-unregister-button"]')).toBeTruthy()
  })

  it('registers a new device from modal form', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    await act(async () => {
      root.render(
        <MemoryRouter>
          <QueryClientProvider client={client}>
            <DeviceListPage />
          </QueryClientProvider>
        </MemoryRouter>,
      )
      await flushPromises()
      await flushPromises()
    })

    await act(async () => {
      ;(container.querySelector('[data-testid="device-register-button"]') as HTMLButtonElement).click()
    })

    const tokenInput = container.querySelector('[data-testid="device-form-token"]') as HTMLInputElement
    const platformSelect = container.querySelector('[data-testid="device-form-platform"]') as HTMLSelectElement

    const setInput = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
    const setSelect = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')?.set

    await act(async () => {
      setInput?.call(tokenInput, 'new-token-123')
      tokenInput.dispatchEvent(new Event('input', { bubbles: true }))
      setSelect?.call(platformSelect, '1')
      platformSelect.dispatchEvent(new Event('change', { bubbles: true }))
      ;(container.querySelector('[data-testid="device-form-submit"]') as HTMLButtonElement).click()
      await flushPromises()
      await flushPromises()
    })

    expect(vi.mocked(registerDevice).mock.calls[0]?.[0]).toEqual({
      token: 'new-token-123',
      platform: 1,
      tenantId: null,
    })
  })
})
