// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { MobileSetupPage } from './MobileSetup'

vi.mock('qrcode.react', () => ({
  QRCodeSVG: ({ value }: { value: string }) => <div data-testid="qr-value">{value}</div>,
}))

describe('MobileSetupPage', () => {
  let root: Root
  let container: HTMLDivElement
  const fetchSpy = vi.fn()

  beforeEach(async () => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    fetchSpy.mockReset()
    vi.stubGlobal('fetch', fetchSpy)
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    await act(async () => {
      root.render(<MobileSetupPage />)
      await new Promise((resolve) => setTimeout(resolve, 0))
    })
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
    vi.unstubAllGlobals()
  })

  it('puts only type, version and serverUrl in the QR payload', () => {
    const value = container.querySelector('[data-testid="qr-value"]')?.textContent ?? ''

    expect(JSON.parse(value)).toStrictEqual({
      type: 'openksef-setup',
      version: 1,
      serverUrl: window.location.origin,
    })
  })

  it('makes no API call on load, so no setup token is requested', () => {
    expect(fetchSpy).not.toHaveBeenCalled()
  })

  it('shows no token countdown, regenerate button or login promise', () => {
    const text = container.textContent ?? ''

    expect(container.querySelector('[data-testid="mobile-setup-countdown"]')).toBeNull()
    expect(container.querySelector('[data-testid="mobile-setup-regenerate"]')).toBeNull()
    expect(text).not.toMatch(/token/i)
    expect(text).not.toContain('zalogować się')
    expect(text).not.toContain('login zostaną')
    expect(text).toContain('Potem zaloguj się w aplikacji.')
    expect(text).toContain('przycisk „QR”')
  })
})
