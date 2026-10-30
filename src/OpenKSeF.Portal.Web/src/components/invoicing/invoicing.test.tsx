// @vitest-environment jsdom
import { act, useState, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { lookupNip } from '@/api/endpoints/nipLookup'
import { ApiError } from '@/api/errors'
import { emptyLine, type InvoiceLineFormValue } from '@/invoicing/form'
import { EnvBadge, KsefStatusBadge } from './KsefStatusBadge'
import { LinesTable } from './LinesTable'
import { NipLookupField } from './NipLookupField'
import { ValidationSummary } from './ValidationSummary'
import { VatRateSelect } from './VatRateSelect'

vi.mock('@/api/endpoints/nipLookup', () => ({ lookupNip: vi.fn() }))

function typeInto(element: HTMLInputElement, value: string): void {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(element, value)
  element.dispatchEvent(new Event('input', { bubbles: true }))
}

describe('issuing components', () => {
  let root: Root
  let container: HTMLDivElement

  beforeEach(() => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    vi.mocked(lookupNip).mockReset()
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(element: ReactElement): Promise<void> {
    await act(async () => root.render(element))
  }

  it('VatRateSelect offers the MVP rates with Polish labels and FA(3) codes', async () => {
    await render(<VatRateSelect id="rate" value="23" onChange={() => {}} />)
    const options = [...container.querySelectorAll('option')].map((option) => [option.value, option.textContent])
    expect(options).toEqual([
      ['23', '23%'],
      ['8', '8%'],
      ['5', '5%'],
      ['0 KR', '0% (kraj)'],
      ['zw', 'zw (zwolnione)'],
    ])
  })

  it('LinesTable recomputes line amounts and adds/removes lines', async () => {
    function Harness(): ReactElement {
      const [lines, setLines] = useState<InvoiceLineFormValue[]>([emptyLine()])
      return <LinesTable lines={lines} onChange={setLines} fieldErrors={{ 'lines[0].name': 'Podaj nazwę' }} />
    }
    await render(<Harness />)

    const price = container.querySelector<HTMLInputElement>('#line-0-unitNetPrice')!
    await act(async () => typeInto(price, '100,50'))
    const row = container.querySelector('[data-testid="line-0"]')!
    expect(row.textContent?.replace(/\s/g, ' ')).toContain('100,50 PLN')
    expect(row.textContent?.replace(/\s/g, ' ')).toContain('23,12 PLN')
    expect(container.querySelector('#line-0-name')?.getAttribute('aria-invalid')).toBe('true')
    expect(container.textContent).toContain('Podaj nazwę')

    const add = [...container.querySelectorAll('button')].find((button) => button.textContent?.includes('Dodaj pozycję'))!
    await act(async () => add.click())
    expect(container.querySelectorAll('tbody tr')).toHaveLength(2)

    await act(async () => container.querySelector<HTMLButtonElement>('[aria-label="Usuń pozycję 2"]')!.click())
    expect(container.querySelectorAll('tbody tr')).toHaveLength(1)
  })

  it('NipLookupField disables lookup for an invalid NIP', async () => {
    await render(<NipLookupField id="nip" value="1234567890" onChange={() => {}} onFound={() => {}} />)
    expect(container.textContent).toContain('Niepoprawny NIP (suma kontrolna)')
    expect(container.querySelector('button')?.disabled).toBe(true)
  })

  it('NipLookupField passes the result on success', async () => {
    const onFound = vi.fn()
    vi.mocked(lookupNip).mockResolvedValue({
      nip: '5260250274',
      name: 'Firma',
      addressLine1: 'ul. Prosta 1',
      addressLine2: '00-001 Warszawa',
      vatStatus: 'Czynny',
      bankAccounts: [],
    })
    await render(<NipLookupField id="nip" value="5260250274" onChange={() => {}} onFound={onFound} />)
    await act(async () => container.querySelector('button')!.click())
    expect(lookupNip).toHaveBeenCalledWith('5260250274')
    expect(onFound).toHaveBeenCalledWith(expect.objectContaining({ name: 'Firma' }))
    expect(container.textContent).toContain('Czynny podatnik VAT')
  })

  it.each([
    [404, 'Nie ma na białej liście — wpisz dane ręcznie'],
    [503, 'Biała lista nie odpowiada, wpisz dane ręcznie'],
  ])('NipLookupField keeps manual entry on %i', async (status, message) => {
    const onFound = vi.fn()
    vi.mocked(lookupNip).mockRejectedValue(new ApiError('x', status))
    await render(<NipLookupField id="nip" value="5260250274" onChange={() => {}} onFound={onFound} />)
    await act(async () => container.querySelector('button')!.click())
    expect(onFound).not.toHaveBeenCalled()
    expect(container.textContent).toContain(message)
  })

  it('status and environment badges use Polish labels', async () => {
    await render(
      <>
        <KsefStatusBadge status="Draft" />
        <KsefStatusBadge status="Sending" />
        <KsefStatusBadge status="Accepted" />
        <KsefStatusBadge status="Rejected" />
        <EnvBadge environment="production" />
      </>,
    )
    const text = container.textContent ?? ''
    for (const label of ['Szkic', 'Wysyłanie', 'Przyjęta w KSeF', 'Odrzucona', 'PRODUKCJA']) {
      expect(text).toContain(label)
    }
  })

  it('ValidationSummary renders errors as alert and warnings as status', async () => {
    await render(
      <ValidationSummary
        errors={[{ code: 'INV-VAL-020', field: 'buyer.nip', message: 'Dla nabywcy firmy wymagany jest poprawny NIP' }]}
        warnings={[{ code: 'INV-W-MPP', field: 'splitPayment', message: 'Sprawdź MPP' }]}
      />,
    )
    expect(container.querySelector('[role="alert"]')?.textContent).toContain('wymagany jest poprawny NIP')
    expect(container.querySelector('[role="status"]')?.textContent).toContain('Sprawdź MPP')
  })
})
