// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import axe from 'axe-core'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import toast from 'react-hot-toast'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { claimTenant } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import { ownerAccess } from '@/test/tenant'
import { ClaimCompanyForm } from './ClaimCompanyForm'

vi.mock('@/api/endpoints/tenants', () => ({ claimTenant: vi.fn() }))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

const TOKEN = 'ref|nip-1111111111|secret'
const failWith = async (status: number, error: string) =>
  vi.mocked(claimTenant).mockRejectedValue(await normalizeError(new Response(JSON.stringify({ error }), { status, headers: { 'Content-Type': 'application/json' } })))

describe('ClaimCompanyForm', () => {
  let root: Root
  let container: HTMLDivElement
  let client: QueryClient
  let router: ReturnType<typeof createMemoryRouter>
  const onCancel = vi.fn()
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)
  const token = () => by('claim-token') as HTMLInputElement | null

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

  async function submit(): Promise<void> {
    await act(async () => by('claim-submit')?.click())
    await settle()
  }

  async function claim(): Promise<void> {
    await type('claim-token', TOKEN)
    await submit()
    await submit()
  }

  beforeEach(async () => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    router = createMemoryRouter(
      [
        { path: '/form', element: <ClaimCompanyForm initialNip="1111111111" onCancel={onCancel} /> },
        { path: '/', element: <p data-testid="start" /> },
      ],
      { initialEntries: ['/form'] },
    )
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <RouterProvider router={router} />
        </QueryClientProvider>,
      ),
    )
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('prefills the NIP and hides the token', () => {
    expect((by('claim-nip') as HTMLInputElement).value).toBe('1111111111')
    expect(token()?.type).toBe('password')
    expect(token()?.getAttribute('autocomplete')).toBe('off')
    expect(container.querySelector('#claim-token-hint')?.textContent).toContain('Zarządzanie uprawnieniami')
  })

  it('has no serious axe violations on both steps', async () => {
    const serious = async () =>
      (await axe.run(container, { rules: { 'color-contrast': { enabled: false } } })).violations.filter((v) => v.impact === 'serious' || v.impact === 'critical').map((v) => v.id)
    expect(await serious()).toEqual([])
    await type('claim-token', TOKEN)
    await submit()
    expect(await serious()).toEqual([])
  })

  it('validates NIP and token before the confirmation', async () => {
    await submit()
    expect(by('claim-error')?.textContent).toBe('Token KSeF jest wymagany')
    await type('claim-nip', '1234567890')
    await type('claim-token', TOKEN)
    await submit()
    expect(by('claim-error')?.textContent).toBe('Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną.')
    expect(by('claim-confirm')).toBeNull()
    expect(claimTenant).not.toHaveBeenCalled()
  })

  it('warns that the current Owners lose access before it posts', async () => {
    await type('claim-token', TOKEN)
    await submit()
    expect(by('claim-confirm')?.textContent).toContain('Obecni właściciele firmy stracą do niej dostęp')
    expect(claimTenant).not.toHaveBeenCalled()
  })

  it('posts nip and token, refreshes companies and opens the claimed one', async () => {
    vi.mocked(claimTenant).mockResolvedValue({ id: 't-9', nip: '1111111111', displayName: 'Firma X', notificationEmail: null, ...ownerAccess, createdAt: '2026-01-01T00:00:00Z' })
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    await claim()
    expect(claimTenant).toHaveBeenCalledExactlyOnceWith({ nip: '1111111111', token: TOKEN })
    expect(invalidate.mock.calls.map(([filters]) => filters?.queryKey)).toEqual([['tenants'], ['dashboard'], ['credentials'], ['onboarding-status']])
    expect(router.state.location.pathname + router.state.location.search).toBe('/?tenantId=t-9')
    expect(toast.success).toHaveBeenCalledWith('Przejęto firmę Firma X.')
  })

  it.each([
    [400, 'Ten token nie potwierdza dostępu do KSeF dla NIP 1111111111. Sprawdź token i NIP.'],
    [409, 'Przejęcie firmy tokenem działa tylko na środowisku PRODUKCJA KSeF. Napisz do administratora serwera.'],
    [429, 'Za dużo prób przejęcia firmy. Spróbuj później.'],
    [503, 'KSeF nie odpowiada. Spróbuj później.'],
  ])('shows the API %i text as is and clears the token', async (status, error) => {
    await failWith(status, error)
    await claim()
    expect(by('claim-error')?.textContent).toBe(error)
    expect(token()?.value).toBe('')
    expect((by('claim-nip') as HTMLInputElement).value).toBe('1111111111')
    expect(router.state.location.pathname).toBe('/form')
  })

  it('cancels with Escape', async () => {
    await act(async () => window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })))
    expect(onCancel).toHaveBeenCalledOnce()
  })
})
