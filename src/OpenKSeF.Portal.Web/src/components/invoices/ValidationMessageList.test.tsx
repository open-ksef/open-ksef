// @vitest-environment jsdom
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ValidationMessageList } from './ValidationMessageList'

describe('ValidationMessageList', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    container.remove()
  })

  it('groups known error codes by family in the expected order', async () => {
    await act(async () => {
      root.render(
        <ValidationMessageList
          stage="Approve"
          messages={[
            createMessage('INV-VAL-060', 'Error', 'Błąd VAT'),
            createMessage('INV-VAL-020', 'Error', 'Błąd daty'),
            createMessage('INV-VAL-010', 'Error', 'Błąd strony'),
            createMessage('INV-VAL-001', 'Error', 'Błąd struktury'),
          ]}
        />,
      )
    })

    const titles = Array.from(container.querySelectorAll('.validation-message-list__group-title')).map(
      (element) => element.textContent,
    )

    expect(titles).toEqual(['Struktura', 'Strony', 'Daty', 'VAT'])
  })

  it('renders error groups above warnings and uses alert role when errors are present', async () => {
    await act(async () => {
      root.render(
        <ValidationMessageList
          stage="Approve"
          messages={[
            createMessage('INV-VAL-064', 'Warning', 'Ostrzeżenie VAT'),
            createMessage('INV-VAL-013', 'Error', 'Błąd nabywcy'),
            createMessage('INV-VAL-021', 'Warning', 'Ostrzeżenie daty'),
          ]}
        />,
      )
    })

    const rootElement = container.querySelector('.validation-message-list')
    const titles = Array.from(container.querySelectorAll('.validation-message-list__group-title')).map(
      (element) => element.textContent,
    )

    expect(rootElement?.getAttribute('role')).toBe('alert')
    expect(titles).toEqual(['Strony', 'Daty', 'VAT'])
  })

  it('uses status role for warnings-only lists', async () => {
    await act(async () => {
      root.render(
        <ValidationMessageList
          stage="Draft"
          messages={[
            createMessage('INV-VAL-021', 'Warning', 'Ostrzeżenie 1'),
            createMessage('INV-VAL-064', 'Warning', 'Ostrzeżenie 2'),
          ]}
        />,
      )
    })

    expect(container.querySelector('.validation-message-list')?.getAttribute('role')).toBe('status')
  })

  it('renders the server-provided Polish text verbatim and shows the rule code', async () => {
    await act(async () => {
      root.render(
        <ValidationMessageList
          stage="SendToKsef"
          messages={[createMessage('INV-VAL-060', 'Error', 'Serwerowy komunikat po polsku.')]}
        />,
      )
    })

    expect(container.textContent).toContain('Serwerowy komunikat po polsku.')
    expect(container.querySelector('.validation-message-list__code')?.textContent).toBe('INV-VAL-060')
  })

  it('warns in development mode for unknown rule codes while still rendering the message', async () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined)

    await act(async () => {
      root.render(
        <ValidationMessageList
          stage="Approve"
          messages={[createMessage('INV-VAL-999', 'Error', 'Nieznany kod nadal widoczny.')]}
        />,
      )
    })

    expect(container.textContent).toContain('Nieznany kod nadal widoczny.')
    expect(warnSpy).toHaveBeenCalledWith('Unknown invoice validation rule code received from backend: INV-VAL-999')
  })
})

function createMessage(
  code: string,
  severity: 'Error' | 'Warning',
  messagePl: string,
  field: string | null = null,
) {
  return {
    code,
    severity,
    field,
    messagePl,
    messageTechnical: `${code} technical message`,
  } as const
}
