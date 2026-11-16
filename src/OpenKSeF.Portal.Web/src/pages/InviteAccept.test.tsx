// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { acceptInvite } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import { useAuth } from '@/auth/useAuth'
import { InviteAcceptPage } from './InviteAccept'

vi.mock('@/api/endpoints/tenants', () => ({ acceptInvite: vi.fn() }))
vi.mock('@/auth/useAuth', () => ({ useAuth: vi.fn() }))

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))
const refusal = (status: number, error: string) =>
  normalizeError(new Response(JSON.stringify({ error }), { status, headers: { 'Content-Type': 'application/json' } }))

describe('InviteAcceptPage', () => {
  let root: Root
  let container: HTMLDivElement
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)

  async function render(): Promise<void> {
    await act(async () =>
      root.render(
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
          <MemoryRouter initialEntries={['/zaproszenie/tok-123']}>
            <Routes>
              <Route path="/zaproszenie/:token" element={<InviteAcceptPage />} />
            </Routes>
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
  }

  async function accept(): Promise<void> {
    await act(async () => by('invite-accept')?.click())
    for (let tick = 0; tick < 5; tick++) await act(flush)
  }

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(useAuth).mockReturnValue({ user: { profile: { sub: 'u', email: 'e@x.pl' } }, logout: vi.fn() } as unknown as ReturnType<typeof useAuth>)
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('accepts only on click, then names the company and role and links to it', async () => {
    vi.mocked(acceptInvite).mockResolvedValue({ tenantId: 't 1', displayName: 'Firma A', role: 'Accountant' })
    await render()
    expect(acceptInvite).not.toHaveBeenCalled()
    expect(by('invite-signed-in-as')?.textContent).toBe('e@x.pl')

    await accept()
    expect(acceptInvite).toHaveBeenCalledExactlyOnceWith('tok-123')
    expect(by('invite-accepted')?.textContent).toBe('Masz dostęp do firmy Firma A. Twoja rola: Księgowy.')
    expect(by('invite-open-company')?.getAttribute('href')).toBe('/?tenantId=t%201')
  })

  it.each([
    [403, 'To zaproszenie jest dla innego adresu e-mail.'],
    [404, 'Zaproszenie jest nieprawidłowe lub wygasło.'],
  ])('shows the API message on %i', async (status, message) => {
    vi.mocked(acceptInvite).mockRejectedValue(await refusal(status, message))
    await render()
    await accept()
    expect(by('invite-accept-error')?.textContent).toBe(`!${message}`)
    expect(by('invite-accepted')).toBeNull()
  })
})
