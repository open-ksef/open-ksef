// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { getSettings } from '@/api/endpoints/system'
import { ApiError } from '@/api/errors'
import { useAuth } from '@/auth/useAuth'
import { SettingsPage } from './Settings'
import { SettingsAdminPage } from './SettingsAdmin'

vi.mock('@/auth/useAuth', () => ({ useAuth: vi.fn() }))
vi.mock('@/api/endpoints/system', () => ({ getSettings: vi.fn(), updateSettings: vi.fn() }))
vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn().mockResolvedValue([]) }))
vi.mock('@/api/endpoints/devices', () => ({ registerDevice: vi.fn() }))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

describe('Ustawienia', () => {
  let root: Root
  let container: HTMLDivElement
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)
  const logout = vi.fn()

  async function render(page: React.ReactElement): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter>{page}</MemoryRouter>
        </QueryClientProvider>,
      ),
    )
  }

  async function login(): Promise<void> {
    const type = (id: string, value: string) => {
      const input = by(id) as HTMLInputElement
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value)
      input.dispatchEvent(new Event('input', { bubbles: true }))
    }
    await act(async () => {
      type('settings-kc-user', 'root')
      type('settings-kc-pass', 'secret')
    })
    await act(async () => by('settings-login')?.click())
    await act(() => new Promise<void>((resolve) => setTimeout(resolve, 0)))
  }

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(useAuth).mockReturnValue({ user: { profile: { name: 'Jan Testowy', email: 'jan@example.invalid' } }, logout } as never)
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('a normal user sees Konto and no admin login form', async () => {
    await render(<SettingsPage />)

    expect(by('account-name')?.textContent).toBe('Jan Testowy')
    expect(by('account-email')?.textContent).toBe('jan@example.invalid')
    expect(by('settings-kc-user')).toBeNull()
    expect(by('account-manage')?.getAttribute('href')).toBe('/auth/realms/openksef/account')
    expect(container.querySelector('a[href="/settings/admin"]')?.textContent).toBe('Administracja instancji')

    await act(async () => by('account-logout')?.click())
    expect(logout).toHaveBeenCalledOnce()
  })

  it('the admin gate has no prefilled user and never says Keycloak', async () => {
    await render(<SettingsAdminPage />)

    expect((by('settings-kc-user') as HTMLInputElement).value).toBe('')
    expect(container.textContent).not.toContain('Keycloak')
  })

  it('a wrong admin login does not show the server text naming Keycloak', async () => {
    vi.mocked(getSettings).mockRejectedValue(new ApiError('Invalid Keycloak admin credentials.', 401))
    await render(<SettingsAdminPage />)
    await login()
    expect(container.querySelector('[role="alert"]')?.textContent).toBe('Nieprawidłowe dane logowania')
  })

  it('keeps every section and field of the admin screen behind the login', async () => {
    vi.mocked(getSettings).mockResolvedValue({
      externalBaseUrl: 'https://x.invalid',
      kSeFEnvironment: 'test',
      registrationAllowed: true,
      verifyEmail: false,
      loginWithEmailAllowed: true,
      resetPasswordAllowed: false,
      passwordPolicy: 'length(8)',
      smtp: { host: 'h', port: '587', from: 'f@x.invalid', starttls: true, ssl: false, auth: true, user: 'u' },
      pushRelayUrl: 'https://relay.invalid',
      firebaseConfigured: false,
      googleConfigured: false,
    } as never)
    await render(<SettingsAdminPage />)
    await login()

    expect([...container.querySelectorAll('h2')].map((heading) => heading.textContent)).toEqual([
      'Podstawowe',
      'Autoryzacja',
      'SMTP',
      'Integracje',
      'Ręczna rejestracja urządzenia',
    ])
    for (const id of ['settings-url', 'settings-ksef-env', 'settings-pass-policy', 'settings-smtp-host', 'settings-smtp-from', 'settings-google-id', 'settings-google-secret', 'settings-relay-url', 'settings-relay-key', 'device-form-token', 'device-form-submit', 'settings-save', 'settings-save-bottom']) {
      expect(by(id), id).not.toBeNull()
    }
    expect([...(by('settings-ksef-env') as HTMLSelectElement).options].map((option) => option.textContent)).toEqual(['Test', 'Produkcja'])
    expect(container.textContent).not.toContain('Keycloak')
  })
})
