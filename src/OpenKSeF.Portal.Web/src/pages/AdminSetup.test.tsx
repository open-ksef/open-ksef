// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { applySetup, authenticateAdmin } from '@/api/endpoints/system'
import { normalizeError } from '@/api/errors'
import { AdminSetupPage } from './AdminSetup'

vi.mock('@/api/endpoints/system', () => ({ authenticateAdmin: vi.fn(), applySetup: vi.fn() }))

const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))

async function waitFor(assertion: () => boolean): Promise<void> {
  for (let i = 0; i < 100; i++) {
    if (assertion()) return
    await act(flush)
  }
  throw new Error('Condition not met')
}

describe('AdminSetupPage', () => {
  let root: Root
  let container: HTMLDivElement

  const byTestId = <T extends HTMLElement>(id: string) => container.querySelector<T>(`[data-testid="${id}"]`)

  const type = (id: string, value: string) =>
    act(() => {
      const element = byTestId<HTMLInputElement>(id)!
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(element, value)
      element.dispatchEvent(new Event('input', { bubbles: true }))
    })

  const click = (id: string) => act(async () => byTestId(id)!.click())

  async function reachSummary(): Promise<void> {
    type('setup-kc-pass', 'kc-password')
    await click('setup-next')
    await waitFor(() => Boolean(byTestId('setup-admin-email')))
    type('setup-admin-email', 'jan@firma.pl')
    type('setup-admin-pass', 'Haslo1234')
    type('setup-tenant-nip', '5260250274')
    await click('setup-next')
    await click('setup-next')
    await click('setup-next')
    await click('setup-next')
    await waitFor(() => Boolean(byTestId('setup-apply')))
  }

  const refusal = (error: string) =>
    normalizeError(
      new Response(JSON.stringify({ success: false, apiClientSecret: null, error }), {
        status: 400,
        headers: { 'Content-Type': 'application/json' },
      }),
    )

  beforeEach(async () => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.clearAllMocks()
    vi.mocked(authenticateAdmin).mockResolvedValue({ setupToken: 'session', expiresInSeconds: 600 })
    await act(async () =>
      root.render(
        <QueryClientProvider client={new QueryClient()}>
          <MemoryRouter>
            <AdminSetupPage />
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('shows the success screen when the response carries no key', async () => {
    vi.mocked(applySetup).mockResolvedValue({ success: true, apiClientSecret: 'secret' })
    await reachSummary()

    await click('setup-apply')
    await waitFor(() => Boolean(byTestId('setup-success')))

    expect(container.textContent).toContain('jan@firma.pl')
    expect(byTestId('setup-apply')).toBeNull()
  })

  it.each([
    'ENCRYPTION_KEY is not set. Generate one with: openssl rand -base64 32, set it on the API and Worker, and restart.',
    'Encryption key is not valid base64. Generate with: openssl rand -base64 32',
    'Encryption key must decode to exactly 32 bytes (got 16). Generate with: openssl rand -base64 32',
    "'ENCRYPTION_KEY_FILE' cannot be read.",
  ])('refusal "%s" explains the key in Polish and restarts at login with the data kept', async (error) => {
    vi.mocked(applySetup).mockRejectedValue(await refusal(error))
    await reachSummary()

    await click('setup-apply')
    await waitFor(() => Boolean(byTestId('setup-kc-pass')))

    const alert = container.querySelector('[role="alert"]')?.textContent
    expect(alert).toContain('Brak prawidłowego klucza szyfrowania')
    expect(alert).toContain('ENCRYPTION_KEY')
    expect(alert).not.toContain(error)
    expect(byTestId('setup-success')).toBeNull()

    await click('setup-next')
    await waitFor(() => Boolean(byTestId('setup-admin-email')))
    expect(byTestId<HTMLInputElement>('setup-admin-email')?.value).toBe('jan@firma.pl')
    expect(authenticateAdmin).toHaveBeenCalledTimes(2)
  })

  it('other refusals stay on the summary with the API message', async () => {
    vi.mocked(applySetup).mockRejectedValue(await refusal('Keycloak unavailable'))
    await reachSummary()

    await click('setup-apply')
    await waitFor(() => Boolean(container.querySelector('[role="alert"]')))

    expect(container.querySelector('[role="alert"]')?.textContent).toContain('Keycloak unavailable')
    expect(byTestId('setup-apply')).not.toBeNull()
  })
})
