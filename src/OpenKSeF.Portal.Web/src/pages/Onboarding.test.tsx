// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import axe from 'axe-core'
import toast from 'react-hot-toast'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { addOrUpdateCredential, forceCredentialSync } from '@/api/endpoints/credentials'
import { claimTenant, createTenant } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import { INVALID_TOKEN_REJECTION } from '@/invoicing/status'
import { OnboardingPage } from './Onboarding'
import { ownerAccess } from '@/test/tenant'

vi.mock('@/api/endpoints/credentials', () => ({ addOrUpdateCredential: vi.fn(), forceCredentialSync: vi.fn() }))
vi.mock('@/api/endpoints/tenants', () => ({ createTenant: vi.fn(), claimTenant: vi.fn() }))
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
    vi.mocked(createTenant).mockResolvedValue({ id: 't-1', nip: '1111111111', displayName: null, notificationEmail: null, ...ownerAccess, createdAt: '2026-01-01T00:00:00Z' })
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

  it('has no serious axe violations on both steps', async () => {
    const serious = async () =>
      (await axe.run(container, { rules: { 'color-contrast': { enabled: false } } })).violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)
    expect(await serious()).toEqual([])
    await toStep2()
    expect(await serious()).toEqual([])
  })

  it.each([
    'Masz już dostęp do firmy z tym NIP-em: Firma C2.',
    'Firma z tym NIP-em jest już dodana na tym serwerze OpenKSeF. Poproś osobę, która nią zarządza, o zaproszenie do firmy. Jeśli nie wiesz, kto to jest, napisz do administratora serwera. Jeśli masz token KSeF z uprawnieniem Zarządzanie uprawnieniami dla tego NIP-u, możesz przejąć firmę w portalu OpenKSeF (Przejmij firmę).',
  ])('shows the API 409 text as is: %s', async (error) => {
    vi.mocked(createTenant).mockRejectedValue(await normalizeError(new Response(JSON.stringify({ error }), { status: 409, headers: { 'Content-Type': 'application/json' } })))
    await toStep2()
    expect(container.querySelector('[role="alert"]')?.textContent).toBe(error)
    expect(toast.error).not.toHaveBeenCalled()
    expect(by('onboarding-claim')).not.toBeNull()
  })

  it('offers Przejmij firmę after a 409 and a claim opens the company', async () => {
    vi.mocked(createTenant).mockRejectedValue(await normalizeError(new Response(JSON.stringify({ error: 'zajęta' }), { status: 409, headers: { 'Content-Type': 'application/json' } })))
    vi.mocked(claimTenant).mockResolvedValue({ id: 't-9', nip: '1111111111', displayName: 'Firma X', notificationEmail: null, ...ownerAccess, createdAt: '2026-01-01T00:00:00Z' })
    expect(by('onboarding-claim')).toBeNull()
    await toStep2()
    await act(async () => by('onboarding-claim')?.click())
    await settle()
    expect((by('claim-nip') as HTMLInputElement).value).toBe('1111111111')
    await type('claim-token', 'ref|nip-1111111111|secret')
    await act(async () => by('claim-submit')?.click())
    await settle()
    await act(async () => by('claim-submit')?.click())
    await settle()
    expect(claimTenant).toHaveBeenCalledExactlyOnceWith({ nip: '1111111111', token: 'ref|nip-1111111111|secret' })
    expect(router.state.location.pathname + router.state.location.search).toBe('/?tenantId=t-9')
    expect(by('start')).not.toBeNull()
  })

  it('does not offer Przejmij firmę after a non-409 error', async () => {
    vi.mocked(createTenant).mockRejectedValue(await normalizeError(new Response('{}', { status: 500 })))
    await toStep2()
    expect(by('onboarding-claim')).toBeNull()
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
    expect(by('onboarding-instruction')?.textContent).toContain('Przeglądanie faktur i Wystawianie faktur')
    expect(by('onboarding-instruction')?.textContent).toContain('Aplikacji Podatnika KSeF')
  })

  it('step 2 lists the token permissions and how to generate the token', async () => {
    await toStep2()

    const card = by('onboarding-permissions')
    expect(card?.querySelector('h2')?.textContent).toBe('Uprawnienia tokenu')
    expect([...(card?.querySelectorAll('li') ?? [])].map((li) => li.textContent)).toEqual([
      'Przeglądanie fakturPobieramy faktury otrzymane i powiadamiamy o nowych.',
      'Wystawianie fakturWysyłamy Twoje faktury do KSeF. Bez tego uprawnienia tylko odbierasz.',
    ])
    expect(by('onboarding-howto')?.querySelector('summary')?.textContent).toBe('Jak wygenerować token')
    expect(by('onboarding-howto')?.querySelectorAll('li')[2].textContent).toBe('Wygeneruj token z uprawnieniami: Przeglądanie faktur i Wystawianie faktur.')
  })

  it('shows a missing token as a field error under the input', async () => {
    await toStep2()
    await next()

    expect(by('onboarding-token-error')?.textContent).toBe('Token KSeF jest wymagany')
    expect(by('onboarding-token')?.getAttribute('aria-invalid')).toBe('true')
    expect(by('onboarding-token')?.getAttribute('aria-describedby')).toContain('onboarding-token-error')
  })

  it('shows a save failure under the input, not as a toast', async () => {
    vi.mocked(addOrUpdateCredential).mockRejectedValue(new Error('Serwer niedostępny'))
    await toStep2()
    await type('onboarding-token', 'token')
    await next()

    expect(by('onboarding-token-error')?.textContent).toBe('Błąd zapisywania tokenu KSeF: Serwer niedostępny')
    expect(toast.error).not.toHaveBeenCalled()
  })

  it('returns to the token step with a field error when KSeF rejects the token', async () => {
    vi.mocked(forceCredentialSync).mockRejectedValue(new Error(INVALID_TOKEN_REJECTION))
    await toStep2()
    await type('onboarding-token', 'token')
    await next()

    expect(by('onboarding-success')).toBeNull()
    expect(container.querySelector('[aria-current="step"]')?.textContent).toContain('Połącz z KSeF')
    expect(by('onboarding-token-error')?.textContent).toBe('KSeF nie przyjął tokenu. Sprawdź, czy skopiowano go w całości i czy nie wygasł.')
    expect(by('onboarding-token')?.getAttribute('aria-invalid')).toBe('true')
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
