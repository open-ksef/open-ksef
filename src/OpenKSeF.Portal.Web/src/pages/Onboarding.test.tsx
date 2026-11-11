// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { addOrUpdateCredential, forceCredentialSync } from '@/api/endpoints/credentials'
import { createTenant } from '@/api/endpoints/tenants'
import { OnboardingPage } from './Onboarding'

vi.mock('@/api/endpoints/credentials', () => ({ addOrUpdateCredential: vi.fn(), forceCredentialSync: vi.fn() }))
vi.mock('@/api/endpoints/tenants', () => ({ createTenant: vi.fn() }))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

describe('Onboarding', () => {
  let root: Root
  let container: HTMLDivElement
  let router: ReturnType<typeof createMemoryRouter>
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)

  async function settle(): Promise<void> {
    for (let tick = 0; tick < 6; tick++) await act(() => new Promise<void>((resolve) => setTimeout(resolve, 0)))
  }

  async function type(id: string, value: string): Promise<void> {
    const input = by(id) as HTMLInputElement
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value)
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })
  }

  async function next(): Promise<void> {
    await act(async () => by('onboarding-next')?.click())
    await settle()
  }

  async function toStep2(): Promise<void> {
    await type('onboarding-nip', '1111111111')
    await next()
  }

  beforeEach(async () => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(createTenant).mockResolvedValue({ id: 't-1', nip: '1111111111', displayName: null, notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' })
    vi.mocked(addOrUpdateCredential).mockResolvedValue({ message: 'ok' })
    vi.mocked(forceCredentialSync).mockResolvedValue({ tenantId: 't-1', fetchedInvoices: 4, newInvoices: 4 } as never)
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    router = createMemoryRouter(
      [
        { path: '/onboarding', element: <OnboardingPage /> },
        { path: '/', element: <p data-testid="start" /> },
      ],
      { initialEntries: ['/onboarding'] },
    )
    await act(async () =>
      root.render(
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
          <RouterProvider router={router} />
        </QueryClientProvider>,
      ),
    )
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('marks the current step and validates the NIP', async () => {
    expect(container.querySelector('[aria-current="step"]')?.textContent).toContain('Dane firmy')
    await type('onboarding-nip', '123')
    await next()
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('NIP musi zawierać dokładnie 10 cyfr')
    expect(createTenant).not.toHaveBeenCalled()
  })

  it('blocks a NIP with a wrong checksum', async () => {
    await type('onboarding-nip', '1234567890')
    await next()
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną.')
    expect(createTenant).not.toHaveBeenCalled()
  })

  it('says the notification e-mail needs server mail', () => {
    const hint = container.querySelector('#onboarding-email-hint')
    expect(hint?.textContent).toBe('E-mail wysyłamy tylko wtedy, gdy administrator serwera skonfigurował pocztę.')
    expect(by('onboarding-email')?.getAttribute('aria-describedby')).toBe('onboarding-email-hint')
  })

  it('step 2 shows only the token form, with the permissions and the MF tool', async () => {
    await toStep2()

    expect(by('onboarding-token')).not.toBeNull()
    expect(container.querySelector('input[type="file"]')).toBeNull()
    expect(container.textContent).not.toMatch(/certyfikat/i)
    expect(by('onboarding-instruction')?.textContent).toContain('odczytu i zapisu')
    expect(by('onboarding-instruction')?.textContent).toContain('Aplikacji Podatnika KSeF')
  })

  it('skip goes straight to Start without saving a token or fetching', async () => {
    await toStep2()
    await act(async () => by('onboarding-skip-token')?.click())
    await settle()

    expect(router.state.location.pathname).toBe('/')
    expect(addOrUpdateCredential).not.toHaveBeenCalled()
    expect(forceCredentialSync).not.toHaveBeenCalled()
  })

  it('a saved token starts the first fetch and step 3 leads to Start', async () => {
    await toStep2()
    await type('onboarding-token', '  token  ')
    await next()

    expect(addOrUpdateCredential).toHaveBeenCalledExactlyOnceWith('t-1', 'token')
    expect(vi.mocked(forceCredentialSync).mock.calls[0][0]).toBe('t-1')
    expect(by('onboarding-success')?.textContent).toContain('NIP: 1111111111')
    expect(by('onboarding-go-dashboard')?.textContent).toBe('Przejdź na Start')
    expect(container.textContent).not.toMatch(/pulpit/i)

    await act(async () => by('onboarding-go-dashboard')?.click())
    await settle()
    expect(router.state.location.pathname).toBe('/')
  })

  it('says so when the first fetch fails', async () => {
    vi.mocked(forceCredentialSync).mockRejectedValue(new Error('502'))
    await toStep2()
    await type('onboarding-token', 'token')
    await next()
    expect(by('onboarding-success')?.textContent).toContain('Pierwsze pobranie się nie udało')
  })
})
