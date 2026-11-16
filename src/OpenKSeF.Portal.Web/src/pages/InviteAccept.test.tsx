// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { RouterProvider, createBrowserRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { acceptInvite } from '@/api/endpoints/tenants'
import { normalizeError } from '@/api/errors'
import { INVITE_TOKEN_KEY } from '@/auth/inviteToken'
import { useAuth } from '@/auth/useAuth'
import { InviteAcceptPage } from './InviteAccept'

vi.mock('@/api/endpoints/tenants', () => ({ acceptInvite: vi.fn() }))
vi.mock('@/auth/useAuth', () => ({ useAuth: vi.fn() }))

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))
const refusal = (status: number, error?: string) =>
  normalizeError(new Response(error ? JSON.stringify({ error }) : null, { status, headers: { 'Content-Type': 'application/json' } }))

describe('InviteAcceptPage', () => {
  let root: Root
  let container: HTMLDivElement
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)
  const href = () => window.location.pathname + window.location.search + window.location.hash

  async function render(url = '/zaproszenie#tok-123'): Promise<void> {
    window.history.replaceState(null, '', url)
    const router = createBrowserRouter([{ path: '/zaproszenie', element: <InviteAcceptPage /> }])
    await act(async () =>
      root.render(
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
          <RouterProvider router={router} />
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
    sessionStorage.clear()
    localStorage.clear()
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

  it('reads the token from the fragment, accepts only on click, then drops it from the address and storage', async () => {
    vi.mocked(acceptInvite).mockResolvedValue({ tenantId: 't 1', displayName: 'Firma A', role: 'Accountant' })
    sessionStorage.setItem(INVITE_TOKEN_KEY, 'stale')
    await render()
    expect(acceptInvite).not.toHaveBeenCalled()
    expect(by('invite-signed-in-as')?.textContent).toBe('e@x.pl')

    await accept()
    expect(acceptInvite).toHaveBeenCalledExactlyOnceWith('tok-123')
    expect(by('invite-accepted')?.textContent).toBe('Masz dostęp do firmy Firma A. Twoja rola: Księgowy.')
    expect(by('invite-open-company')?.getAttribute('href')).toBe('/?tenantId=t%201')
    expect(href()).toBe('/zaproszenie')
    expect(sessionStorage.getItem(INVITE_TOKEN_KEY)).toBeNull()
    expect(JSON.stringify({ ...localStorage })).not.toContain('tok-123')
  })

  it('takes the token kept in sessionStorage across the login redirect', async () => {
    vi.mocked(acceptInvite).mockResolvedValue({ tenantId: 't', displayName: 'Firma A', role: 'Employee' })
    sessionStorage.setItem(INVITE_TOKEN_KEY, 'tok-kept')
    await render('/zaproszenie')
    await accept()
    expect(acceptInvite).toHaveBeenCalledExactlyOnceWith('tok-kept')
    expect(sessionStorage.getItem(INVITE_TOKEN_KEY)).toBeNull()
  })

  it.each([
    [403, 'To zaproszenie jest dla innego adresu e-mail.'],
    [404, 'Zaproszenie jest nieprawidłowe lub wygasło.'],
  ])('shows the API message on %i and drops the token', async (status, message) => {
    vi.mocked(acceptInvite).mockRejectedValue(await refusal(status, message))
    await render()
    await accept()
    expect(by('invite-accept-error')?.textContent).toBe(`!${message}`)
    expect(by('invite-accepted')).toBeNull()
    expect(href()).toBe('/zaproszenie')
  })

  it('keeps the token for a retry after a bodyless 429', async () => {
    vi.mocked(acceptInvite).mockRejectedValue(await refusal(429))
    await render()
    await accept()
    expect(by('invite-accept-error')?.textContent).toBe('!Za dużo żądań. Spróbuj ponownie za chwilę.')
    expect(href()).toBe('/zaproszenie#tok-123')
  })

  it('says the invite is invalid when there is no token', async () => {
    await render('/zaproszenie')
    expect(by('invite-accept-error')?.textContent).toBe('!Zaproszenie jest nieprawidłowe lub wygasło.')
    expect((by('invite-accept') as HTMLButtonElement).disabled).toBe(true)
  })
})
