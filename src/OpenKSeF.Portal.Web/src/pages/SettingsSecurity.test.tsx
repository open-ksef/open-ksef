// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import toast from 'react-hot-toast'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { deleteCredential, getCredentials, getLinkedAccounts, getSessionDevices, type AccountCredentialType } from '@/auth/account'
import { useAuth } from '@/auth/useAuth'
import { SettingsSecurityPage } from './SettingsSecurity'

vi.mock('@/auth/useAuth', () => ({ useAuth: vi.fn() }))
vi.mock('@/auth/signOutAll', () => ({ signOutAllDevices: vi.fn() }))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))
vi.mock('@/auth/account', async (original) => ({
  ...(await original<object>()),
  getCredentials: vi.fn(),
  getLinkedAccounts: vi.fn(),
  getSessionDevices: vi.fn(),
  deleteCredential: vi.fn(),
}))

const password = { type: 'password', userCredentialMetadatas: [{ credential: { id: 'pw', type: 'password', createdDate: Date.UTC(2026, 9, 3) } }] }
const otp = (...ids: string[]) => ({ type: 'otp', userCredentialMetadatas: ids.map((id, i) => ({ credential: { id, type: 'otp', createdDate: Date.UTC(2026, 10, 14) + i } })) })
const codes = (remainingCodes: number) => ({
  type: 'recovery-authn-codes',
  userCredentialMetadatas: [{ credential: { id: 'rc', type: 'recovery-authn-codes', credentialData: JSON.stringify({ totalCodes: 12, remainingCodes }) } }],
})

describe('Ustawienia > Bezpieczeństwo', () => {
  let root: Root
  let container: HTMLDivElement
  const startAction = vi.fn()
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)

  async function render(credentials: AccountCredentialType[], { google = false, path = '/settings/security' } = {}): Promise<void> {
    vi.mocked(getCredentials).mockResolvedValue(credentials)
    vi.mocked(getLinkedAccounts).mockResolvedValue(google ? [{ providerAlias: 'google', connected: true }] : [])
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={[path]}><SettingsSecurityPage /></MemoryRouter>
        </QueryClientProvider>,
      ),
    )
    for (let tick = 0; tick < 3; tick++) await act(() => new Promise<void>((resolve) => setTimeout(resolve, 0)))
  }

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(useAuth).mockReturnValue({ startAction } as never)
    vi.mocked(getSessionDevices).mockResolvedValue([
      { id: 'd1', ipAddress: '192.0.2.10', os: 'Windows', browser: 'Edge/131.0', lastAccess: 1, current: true, sessions: [{ id: 's1', ipAddress: '192.0.2.10', started: 1_763_100_000, lastAccess: 1_763_100_100, browser: 'Edge/131.0', current: true, clients: [{ clientId: 'openksef-portal-web' }] }] },
      { id: 'd2', ipAddress: '198.51.100.7', os: 'Android', lastAccess: 1, sessions: [{ id: 's2', ipAddress: '198.51.100.7', started: 1_762_100_000, lastAccess: 1_763_000_000, clients: [{ clientId: 'openksef-mobile' }] }] },
    ])
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('shows an enabled 2FA with codes left and starts Keycloak actions', async () => {
    await render([password, otp('o1'), codes(9)])

    expect(by('tfa-status')?.textContent).toBe('Włączona')
    expect(by('tfa-codes')?.textContent).toBe('Zostało 9 z 12')
    expect(by('password-changed')?.textContent).toBe('03.10.2026')
    expect(by('tfa-regenerate')?.className).toContain('ui-button--outline')

    await act(async () => by('tfa-change')?.click())
    expect(startAction).toHaveBeenCalledWith('CONFIGURE_TOTP')

    await act(async () => by('tfa-regenerate')?.click())
    expect(container.querySelector('[role="alertdialog"]')?.textContent).toContain('Wygenerować nowe kody zapasowe?')
    await act(async () => container.querySelector<HTMLElement>('.ui-modal-footer .ui-button--primary')?.click())
    expect(startAction).toHaveBeenCalledWith('CONFIGURE_RECOVERY_AUTHN_CODES')

    await act(async () => by('password-change')?.click())
    expect(startAction).toHaveBeenCalledWith('UPDATE_PASSWORD')
  })

  it.each([
    [2, 'Zostały tylko 2 kody zapasowe. Wygeneruj nowe, zanim się skończą.'],
    [1, 'Został tylko 1 kod zapasowy. Wygeneruj nowe, zanim się skończą.'],
    [0, 'Nie masz już kodów zapasowych. Wygeneruj nowe.'],
  ])('warns with %i codes left and makes regenerate primary', async (left, text) => {
    await render([password, otp('o1'), codes(left)])

    expect(container.querySelector('.auth-notice--warning')?.textContent).toBe(text)
    expect(by('tfa-regenerate')?.className).toContain('ui-button--primary')
  })

  it('offers setup now when 2FA is not set', async () => {
    await render([password])

    expect(by('tfa-status')?.textContent).toBe('Nieustawiona')
    await act(async () => by('tfa-setup')?.click())
    expect(startAction).toHaveBeenCalledWith('CONFIGURE_TOTP')
  })

  it('hides the password card for a Google-only account', async () => {
    await render([], { google: true })

    expect(container.textContent).not.toContain('Hasło')
    expect(container.textContent).toContain('Przez Google')
    expect(by('tfa-setup')).toBeNull()
  })

  it('removes the older authenticator after a successful app change', async () => {
    vi.mocked(deleteCredential).mockResolvedValue()
    await render([password, otp('old', 'new'), codes(12)], { path: '/settings/security?action=CONFIGURE_TOTP&kc_action_status=success' })

    expect(toast.success).toHaveBeenCalledWith('Zapisano.')
    expect(deleteCredential).toHaveBeenCalledTimes(1)
    expect(deleteCredential).toHaveBeenCalledWith('old')
  })

  it('keeps older authenticators after another successful action', async () => {
    await render([password, otp('old', 'new'), codes(12)], { path: '/settings/security?action=UPDATE_PASSWORD&kc_action_status=success' })

    expect(toast.success).toHaveBeenCalledWith('Zapisano.')
    expect(deleteCredential).not.toHaveBeenCalled()
  })

  it('says nothing and deletes nothing after a cancelled action', async () => {
    await render([password, otp('old', 'new'), codes(12)], { path: '/settings/security?kc_action_status=cancelled' })

    expect(toast.success).not.toHaveBeenCalled()
    expect(deleteCredential).not.toHaveBeenCalled()
  })

  it('lists sessions with this browser and the mobile app', async () => {
    await render([password, otp('o1'), codes(9)])

    const rows = [...container.querySelectorAll('.settings-sessions-table tbody tr')].map((row) => row.textContent)
    expect(rows[0]).toContain('Edge, Windows')
    expect(rows[0]).toContain('Ta przeglądarka · 192.0.2.10')
    expect(rows[0]).toContain('teraz')
    expect(rows[1]).toContain('Telefon: Android')
    expect(rows[1]).toContain('Aplikacja OpenKSeF')
  })
})
