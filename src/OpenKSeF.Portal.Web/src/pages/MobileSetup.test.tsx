// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { listDevices, sendTestNotification, unregisterDevice } from '@/api/endpoints/devices'
import { listTenants } from '@/api/endpoints/tenants'
import { signOutAllDevices } from '@/auth/signOutAll'
import toast from 'react-hot-toast'
import { MobileSetupPage } from './MobileSetup'

const { qrValue, pairing } = vi.hoisted(() => ({ qrValue: vi.fn(), pairing: { enabled: false } }))

vi.mock('qrcode.react', () => ({
  QRCodeSVG: ({ value }: { value: string }) => {
    qrValue(value)
    return <svg data-testid="qr" />
  },
}))
vi.mock('@/api/endpoints/devices', () => ({ listDevices: vi.fn(), sendTestNotification: vi.fn(), unregisterDevice: vi.fn() }))
vi.mock('@/api/endpoints/tenants', () => ({ listTenants: vi.fn() }))
vi.mock('@/auth/signOutAll', () => ({ signOutAllDevices: vi.fn() }))
vi.mock('@/auth/config', () => ({ oidcConfig: { authority: '/auth/realms/openksef' } }))
vi.mock('@/auth/pairing', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/auth/pairing')>()),
  get PAIRING_CODE_ENABLED() {
    return pairing.enabled
  },
}))
vi.mock('react-hot-toast', () => ({ default: { error: vi.fn(), success: vi.fn() } }))

const RAW_TOKEN = 'fcm-token-abcdefghijklmnopqrstuvwxyz-1234'
const device = {
  id: 'd-1',
  token: RAW_TOKEN,
  platform: 0,
  tenantId: null,
  createdAt: '2026-10-12T10:00:00Z',
  updatedAt: '2026-11-02T10:00:00Z',
}

describe('Telefon', () => {
  let root: Root
  let container: HTMLDivElement
  const by = (id: string) => container.querySelector<HTMLElement>(`[data-testid="${id}"]`)

  async function render(): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <MemoryRouter>
            <MobileSetupPage />
          </MemoryRouter>
        </QueryClientProvider>,
      ),
    )
    for (let tick = 0; tick < 6; tick++) await act(() => new Promise<void>((resolve) => setTimeout(resolve, 0)))
  }

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    vi.mocked(listDevices).mockResolvedValue([device, { ...device, id: 'd-2', platform: 1, tenantId: 't-1' }] as never)
    vi.mocked(listTenants).mockResolvedValue([{ id: 't-1', nip: '1111111111', displayName: 'Firma A', notificationEmail: null, createdAt: '2026-01-01T00:00:00Z' }])
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  it('puts only type, version and serverUrl in the QR payload', async () => {
    await render()
    const payload = JSON.parse(qrValue.mock.calls[0][0] as string)
    expect(Object.keys(payload).sort()).toEqual(['serverUrl', 'type', 'version'])
    expect(payload).toMatchObject({ type: 'openksef-setup', version: 1 })
  })

  it('shows no token countdown, regenerate button or login promise', async () => {
    await render()
    const text = container.textContent ?? ''
    expect(text).not.toMatch(/wygasa|odśwież|regener/i)
    expect(text).toContain('Potem zaloguj się w aplikacji')
  })

  it('reads devices by platform and dates, with no raw token and no emoji', async () => {
    await render()
    const text = by('device-table')?.textContent ?? ''
    expect(text).toContain('Telefon: Android')
    expect(text).toContain('Telefon: iOS')
    expect(text).toContain('Dodany 12.10.2026, aktualizacja 02.11.2026')
    expect(text).toContain('Wszystkie firmy')
    expect(text).toContain('Firma A')
    expect(text).not.toContain(RAW_TOKEN)
    expect(text).not.toContain(RAW_TOKEN.slice(0, 8))
    expect(by('device-table')?.innerHTML).not.toContain(RAW_TOKEN)
    expect(text).not.toMatch(/\p{Extended_Pictographic}/u)
  })

  it('has no manual registration on the normal screen', async () => {
    await render()
    expect(container.querySelector('[data-testid="device-register-button"]')).toBeNull()
    expect(container.textContent).not.toContain('Zarejestruj')
  })

  it('sends a test and removes a device only after a dialog', async () => {
    vi.mocked(sendTestNotification).mockResolvedValue({ success: true } as never)
    vi.mocked(unregisterDevice).mockResolvedValue(undefined as never)
    await render()

    expect(by('device-test-button')?.textContent).toBe('Wyślij test')
    await act(async () => by('device-test-button')?.click())
    expect(vi.mocked(sendTestNotification).mock.calls[0][0]).toBe('d-1')

    await act(async () => by('device-unregister-button')?.click())
    expect(unregisterDevice).not.toHaveBeenCalled()
    const dialog = container.querySelector('[role="alertdialog"]')!
    expect(dialog.textContent).toContain('Telefon: Android, dodany 12.10.2026')
    await act(async () => dialog.querySelector<HTMLElement>('.ui-button--danger')?.click())
    expect(vi.mocked(unregisterDevice).mock.calls[0][0]).toBe('d-1')
  })

  it('signs out all devices after a confirm that warns about the remaining token time', async () => {
    vi.mocked(signOutAllDevices).mockResolvedValue()
    await render()

    act(() => by('sign-out-all-button')!.click())
    const dialog = container.querySelector('[role="alertdialog"]')!
    expect(dialog.textContent).toContain('może działać jeszcze do 30 minut')
    expect(signOutAllDevices).not.toHaveBeenCalled()

    await act(async () => [...dialog.querySelectorAll('button')].find((b) => b.textContent === 'Wyloguj wszystkie')!.click())

    expect(signOutAllDevices).toHaveBeenCalledTimes(1)
    expect(toast.error).not.toHaveBeenCalled()
  })

  it('shows an error and no success when sign-out-all fails', async () => {
    vi.mocked(signOutAllDevices).mockRejectedValue(new Error('Sesja wygasła. Zaloguj się ponownie.'))
    await render()

    act(() => by('sign-out-all-button')!.click())
    await act(async () => [...container.querySelectorAll('[role="alertdialog"] button')].find((b) => b.textContent === 'Wyloguj wszystkie')!.dispatchEvent(new MouseEvent('click', { bubbles: true })))

    expect(toast.error).toHaveBeenCalledWith('Nie udało się wylogować wszystkich urządzeń: Sesja wygasła. Zaloguj się ponownie. Sesje mogą nadal działać. Spróbuj ponownie.')
    expect(toast.success).not.toHaveBeenCalled()
    expect(container.querySelector('[role="alertdialog"]')).toBeNull()
  })

  it('hides the pairing code card', async () => {
    await render()

    expect(by('pairing-code-input')).toBeNull()
    expect(container.textContent).not.toContain('Zaloguj telefon kodem')
  })

  describe('pairing code', () => {
    const assign = vi.fn()

    beforeEach(() => {
      pairing.enabled = true
      vi.stubGlobal('location', { ...window.location, origin: 'https://ksef.example.test', assign })
    })

    afterEach(() => {
      vi.unstubAllGlobals()
      pairing.enabled = false
    })

    async function submit(value: string): Promise<void> {
      const input = by('pairing-code-input') as HTMLInputElement
      await act(async () => {
        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, value)
        input.dispatchEvent(new Event('input', { bubbles: true }))
      })
      await act(async () => by('pairing-code-submit')!.click())
    }

    it.each(['abcd-efgh', 'ABCDEFGH', 'ab cd ef gh'])('opens the Keycloak device page for %j', async (value) => {
      await render()
      await submit(value)

      expect(assign).toHaveBeenCalledWith('/auth/realms/openksef/device?user_code=ABCD-EFGH')
      expect(container.querySelector('#pairing-code-error')).toBeNull()
    })

    it('shows an inline message and goes nowhere for a wrong code', async () => {
      await render()
      await submit('ABC-123')

      expect(assign).not.toHaveBeenCalled()
      expect(container.querySelector('#pairing-code-error')?.textContent).toContain('8 liter')
      expect(by('pairing-code-input')?.getAttribute('aria-invalid')).toBe('true')
    })

    it('warns about foreign codes, the app version and the server address, with no copyable link', async () => {
      await render()
      const text = container.textContent ?? ''

      expect(text).toContain('Wpisuj tylko kod z własnego telefonu')
      expect(text).toContain('Sparuj z portalem')
      expect(text).toContain('najnowszej wersji aplikacji')
      expect(text).toContain('tym samym adresem')
      expect(text).toContain('https://ksef.example.test')
      expect(text).not.toContain('/device')
      expect(container.querySelector('a[href*="device"]')).toBeNull()
    })
  })
})
