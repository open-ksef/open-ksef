// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, useState, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { listLineSuggestions, listRecentBuyers } from '@/api/endpoints/issuedInvoices'
import { lookupNip } from '@/api/endpoints/nipLookup'
import { ApiError } from '@/api/errors'
import type { BuyerKind, NipLookupResponse } from '@/api/types'
import { emptyDraft, emptyLine, withBuyer, type BuyerFormValue, type DraftFormValue, type InvoiceLineFormValue } from '@/invoicing/form'
import { FOREIGN_VAT_RATES } from '@/invoicing/totals'
import { BuyerSection } from './BuyerSection'
import { EnvBadge, KsefStatusBadge } from './KsefStatusBadge'
import { LinesTable } from './LinesTable'
import { NipLookupField } from './NipLookupField'
import { ValidationSummary } from './ValidationSummary'
import { VatRateSelect } from './VatRateSelect'

vi.mock('@/api/endpoints/nipLookup', () => ({ lookupNip: vi.fn() }))
vi.mock('@/api/endpoints/issuedInvoices', () => ({ listRecentBuyers: vi.fn(), listLineSuggestions: vi.fn() }))

const debounce = async () => {
  await act(() => new Promise<void>((resolve) => setTimeout(resolve, 350)))
  await act(() => new Promise<void>((resolve) => setTimeout(resolve, 0)))
}

function typeInto(element: HTMLInputElement, value: string): void {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(element, value)
  element.dispatchEvent(new Event('input', { bubbles: true }))
}

function typeIntoAct(element: HTMLInputElement, value: string): void {
  act(() => typeInto(element, value))
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
    vi.mocked(listRecentBuyers).mockReset()
    vi.mocked(listLineSuggestions).mockReset()
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(element: ReactElement): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await act(async () => root.render(<QueryClientProvider client={client}>{element}</QueryClientProvider>))
  }

  const button = (text: string) => [...container.querySelectorAll('button')].find((item) => item.textContent?.includes(text))

  function DraftHarness({ initial }: { initial: DraftFormValue }): ReactElement {
    const [form, setForm] = useState(initial)
    return (
      <>
        <BuyerSection tenantId="t-1" value={form.buyer} onChange={(buyer) => setForm(withBuyer(form, buyer))} />
        <output data-testid="currency">{form.currency}</output>
        <output data-testid="lines">{form.lines.map((line) => line.name).join('|')}</output>
        <output data-testid="dates">{[form.issueDate, form.dueDate].join('|')}</output>
      </>
    )
  }

  it('Ostatni nabywcy: picking a GB buyer without a tax id fills kind and buyer fields, keeps currency, lines and dates', async () => {
    const initial = { ...emptyDraft('2026-11-24'), currency: 'GBP', lines: [{ ...emptyLine('np I'), name: 'Consulting' }] }
    initial.buyer = { ...initial.buyer, kind: 'Foreign', countryCode: 'US', foreignId: 'US-1' }
    vi.mocked(listRecentBuyers).mockResolvedValue([
      {
        buyer: { kind: 'Foreign', nip: null, name: 'Example Buyer Ltd', addressLine1: '1 Test Street', addressLine2: 'Testville', countryCode: 'GB', idType: 'None', foreignId: null },
        lastIssueDate: '2026-11-01',
      },
      { buyer: { kind: 'Company', nip: '5260250274', name: 'Firma Testowa', addressLine1: 'ul. A 1', addressLine2: '00-001 Warszawa' }, lastIssueDate: '2026-10-01' },
    ])
    await render(<DraftHarness initial={initial} />)
    expect(listRecentBuyers).not.toHaveBeenCalled()

    await act(async () => button('Ostatni nabywcy')!.click())
    await debounce()
    expect(button('Ostatni nabywcy')!.getAttribute('aria-expanded')).toBe('true')
    expect(document.activeElement?.id).toBe('recent-buyers-q')
    expect(container.querySelector('#recent-buyers')?.textContent).toContain('NIP 5260250274')
    await act(async () => button('Example Buyer Ltd')!.click())

    expect(container.querySelector('#recent-buyers')).toBeNull()
    expect(button('Firma zagraniczna')!.getAttribute('aria-pressed')).toBe('true')
    expect(container.querySelector<HTMLSelectElement>('#buyer-countryCode')!.value).toBe('GB')
    expect(container.querySelector<HTMLInputElement>('.inv-check input[type="checkbox"]')!.checked).toBe(true)
    expect(container.querySelector<HTMLInputElement>('#buyer-name')!.value).toBe('Example Buyer Ltd')
    expect(container.querySelector<HTMLInputElement>('#buyer-addressLine1')!.value).toBe('1 Test Street')
    expect(container.querySelector<HTMLInputElement>('#buyer-addressLine2')!.value).toBe('Testville')
    expect(container.querySelector('[data-testid="currency"]')!.textContent).toBe('GBP')
    expect(container.querySelector('[data-testid="lines"]')!.textContent).toBe('Consulting')
    expect(container.querySelector('[data-testid="dates"]')!.textContent).toBe('2026-11-24|2026-12-08')
  })

  it('Ostatni nabywcy searches by the typed fragment and says when nothing matches', async () => {
    vi.mocked(listRecentBuyers).mockResolvedValue([])
    await render(<DraftHarness initial={emptyDraft('2026-11-24')} />)
    await act(async () => button('Ostatni nabywcy')!.click())
    typeIntoAct(container.querySelector<HTMLInputElement>('#recent-buyers-q')!, '5260')
    await debounce()
    expect(listRecentBuyers).toHaveBeenLastCalledWith('t-1', '5260')
    expect(container.textContent).toContain('Brak nabywców dla tego wyszukiwania.')
  })

  it.each(['Firma zagraniczna', 'Osoba prywatna'])('no NIP lookup button for %s', async (label) => {
    await render(<DraftHarness initial={emptyDraft('2026-11-24')} />)
    expect(button('Pobierz z NIP')).toBeDefined()
    await act(async () => button(label)!.click())
    expect(button('Pobierz z NIP')).toBeUndefined()
  })

  function LinesHarness({ buyerKind, foreign = false }: { buyerKind: BuyerKind; foreign?: boolean }): ReactElement {
    const [lines, setLines] = useState<InvoiceLineFormValue[]>([{ ...emptyLine(foreign ? 'np I' : '23'), quantity: '3' }])
    return <LinesTable currency="PLN" lines={lines} rates={foreign ? FOREIGN_VAT_RATES : undefined} onChange={setLines} suggest={{ tenantId: 't-1', buyerKind }} />
  }

  it('line suggestions start at 2 characters; picking fills name, unit, price and rate, quantity stays; no np rates for Firma', async () => {
    vi.mocked(listLineSuggestions).mockResolvedValue([
      { name: 'Usługa księgowa - pakiet S', unit: 'usł.', unitNetPrice: 500, vatRate: '8' },
      { name: 'Usługa księgowa - eksport', unit: 'usł.', unitNetPrice: 100, vatRate: 'np I' },
    ])
    await render(<LinesHarness buyerKind="Company" />)
    const name = container.querySelector<HTMLInputElement>('#line-0-name')!
    await act(async () => name.focus())
    typeIntoAct(name, 'k')
    await debounce()
    expect(listLineSuggestions).not.toHaveBeenCalled()

    typeIntoAct(name, 'księg')
    await debounce()
    expect(listLineSuggestions).toHaveBeenCalledWith('t-1', 'księg', 'Company')
    expect(name.getAttribute('list')).toBe('line-suggestions')
    const options = [...container.querySelectorAll('#line-suggestions option')]
    expect(options.map((option) => option.getAttribute('value'))).toEqual(['Usługa księgowa - pakiet S'])
    expect(options[0].getAttribute('label')).toBe('usł. · 500,00 PLN · 8%')

    typeIntoAct(name, 'Usługa księgowa - pakiet S')
    expect(container.querySelector<HTMLInputElement>('#line-0-unit')!.value).toBe('usł.')
    expect(container.querySelector<HTMLInputElement>('#line-0-unitNetPrice')!.value).toBe('500')
    expect(container.querySelector<HTMLSelectElement>('#line-0-vatRate')!.value).toBe('8')
    expect(container.querySelector<HTMLInputElement>('#line-0-quantity')!.value).toBe('3')
  })

  it('a foreign buyer gets only np suggestions', async () => {
    vi.mocked(listLineSuggestions).mockResolvedValue([
      { name: 'Consulting', unit: 'h', unitNetPrice: 50, vatRate: 'np I' },
      { name: 'Consulting PL', unit: 'h', unitNetPrice: 50, vatRate: '23' },
    ])
    await render(<LinesHarness buyerKind="Foreign" foreign />)
    const name = container.querySelector<HTMLInputElement>('#line-0-name')!
    await act(async () => name.focus())
    typeIntoAct(name, 'Co')
    await debounce()
    expect(listLineSuggestions).toHaveBeenCalledWith('t-1', 'Co', 'Foreign')
    expect([...container.querySelectorAll('#line-suggestions option')].map((option) => option.getAttribute('value'))).toEqual(['Consulting'])
  })

  it('VatRateSelect offers the MVP rates with Polish labels and FA(3) codes', async () => {
    await render(<VatRateSelect id="rate" value="23" onChange={() => {}} />)
    const options = [...container.querySelectorAll('option')].map((option) => [option.value, option.textContent])
    expect(options).toEqual([
      ['23', '23%'],
      ['8', '8%'],
      ['5', '5%'],
      ['0 KR', '0% (kraj)'],
      ['zw', 'zw'],
    ])
  })

  it('LinesTable recomputes line amounts and adds/removes lines', async () => {
    function Harness(): ReactElement {
      const [lines, setLines] = useState<InvoiceLineFormValue[]>([emptyLine()])
      return <LinesTable currency="PLN" lines={lines} onChange={setLines} fieldErrors={{ 'lines[0].name': 'Podaj nazwę' }} />
    }
    await render(<Harness />)

    const price = container.querySelector<HTMLInputElement>('#line-0-unitNetPrice')!
    await act(async () => typeInto(price, '100,50'))
    const row = container.querySelector('[data-testid="line-0"]')!
    expect(row.textContent?.replace(/\s/g, ' ')).toContain('123,62 PLN')
    expect(container.querySelector('#line-0-name')?.getAttribute('aria-invalid')).toBe('true')
    expect(container.textContent).toContain('Podaj nazwę')

    const add = [...container.querySelectorAll('button')].find((button) => button.textContent?.includes('Dodaj pozycję'))!
    await act(async () => add.click())
    expect(container.querySelectorAll('.inv-line')).toHaveLength(2)

    await act(async () => container.querySelector<HTMLButtonElement>('[aria-label="Usuń pozycję 2"]')!.click())
    expect(container.querySelectorAll('.inv-line')).toHaveLength(1)
  })

  it('every line field has a real label naming its line', async () => {
    await render(<LinesTable currency="PLN" lines={[emptyLine(), emptyLine()]} onChange={() => {}} />)
    for (const [name, label] of [['name', 'Nazwa'], ['unit', 'J.m.'], ['quantity', 'Ilość'], ['unitNetPrice', 'Cena netto'], ['vatRate', 'Stawka']]) {
      expect(container.querySelector(`label[for="line-1-${name}"]`)?.textContent).toBe(`${label} pozycji 2`)
    }
  })

  it('NipLookupField shows the loading state and blocks a second lookup', async () => {
    let finish: (value: NipLookupResponse) => void = () => {}
    vi.mocked(lookupNip).mockReturnValue(new Promise((resolve) => (finish = resolve)))
    await render(<NipLookupField id="nip" value="4444444444" onChange={() => {}} onFound={() => {}} />)
    const button = container.querySelector('button')!
    await act(async () => button.click())
    expect(button.getAttribute('aria-busy')).toBe('true')
    expect(button.disabled).toBe(true)
    await act(async () => finish({ nip: '4444444444', name: 'F', addressLine1: null, addressLine2: null, vatStatus: 'Zwolniony', bankAccounts: [] }))
    expect(button.disabled).toBe(false)
    expect(container.textContent).toContain('Zwolniony')
  })

  it.each([
    [false, 'Wpisana nazwa'],
    [true, 'Firma z listy'],
  ])('BuyerSection lookup over typed data asks first, confirmed %s', async (confirmed, expected) => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(confirmed)
    vi.mocked(lookupNip).mockResolvedValue({ nip: '4444444444', name: 'Firma z listy', addressLine1: 'ul. A', addressLine2: '00-001 B', vatStatus: 'Czynny', bankAccounts: [] })
    function Harness(): ReactElement {
      const [buyer, setBuyer] = useState<BuyerFormValue>({ ...emptyDraft().buyer, kind: 'Company', nip: '4444444444', name: 'Wpisana nazwa', addressLine1: '', addressLine2: '' })
      return <BuyerSection value={buyer} onChange={setBuyer} />
    }
    await render(<Harness />)
    const lookup = [...container.querySelectorAll('button')].find((button) => button.textContent?.includes('Pobierz z NIP'))!
    await act(async () => lookup.click())
    expect(confirm).toHaveBeenCalledOnce()
    expect(container.querySelector<HTMLInputElement>('#buyer-name')?.value).toBe(expected)
    confirm.mockRestore()
  })

  it('BuyerSection fills empty fields without asking', async () => {
    const confirm = vi.spyOn(window, 'confirm')
    vi.mocked(lookupNip).mockResolvedValue({ nip: '4444444444', name: 'Firma z listy', addressLine1: null, addressLine2: null, vatStatus: 'Czynny', bankAccounts: [] })
    function Harness(): ReactElement {
      const [buyer, setBuyer] = useState<BuyerFormValue>({ ...emptyDraft().buyer, kind: 'Company', nip: '4444444444', name: '', addressLine1: '', addressLine2: '' })
      return <BuyerSection value={buyer} onChange={setBuyer} />
    }
    await render(<Harness />)
    await act(async () => [...container.querySelectorAll('button')].find((button) => button.textContent?.includes('Pobierz z NIP'))!.click())
    expect(confirm).not.toHaveBeenCalled()
    expect(container.querySelector<HTMLInputElement>('#buyer-name')?.value).toBe('Firma z listy')
    confirm.mockRestore()
  })

  it('NipLookupField disables lookup for an invalid NIP', async () => {
    await render(<NipLookupField id="nip" value="1234567890" onChange={() => {}} onFound={() => {}} />)
    expect(container.textContent).toContain('Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną.')
    expect(container.querySelector('button')?.disabled).toBe(true)
  })

  it('NipLookupField passes the result on success', async () => {
    const onFound = vi.fn()
    vi.mocked(lookupNip).mockResolvedValue({
      nip: '4444444444',
      name: 'Firma',
      addressLine1: 'ul. Prosta 1',
      addressLine2: '00-001 Warszawa',
      vatStatus: 'Czynny',
      bankAccounts: [],
    })
    await render(<NipLookupField id="nip" value="4444444444" onChange={() => {}} onFound={onFound} />)
    await act(async () => container.querySelector('button')!.click())
    expect(lookupNip).toHaveBeenCalledWith('4444444444')
    expect(onFound).toHaveBeenCalledWith(expect.objectContaining({ name: 'Firma' }))
    expect(container.textContent).toContain('Czynny podatnik VAT')
  })

  it.each([
    [404, 'Nie ma na białej liście — wpisz dane ręcznie'],
    [503, 'Biała lista nie odpowiada, wpisz dane ręcznie'],
  ])('NipLookupField keeps manual entry on %i', async (status, message) => {
    const onFound = vi.fn()
    vi.mocked(lookupNip).mockRejectedValue(new ApiError('x', status))
    await render(<NipLookupField id="nip" value="4444444444" onChange={() => {}} onFound={onFound} />)
    await act(async () => container.querySelector('button')!.click())
    expect(onFound).not.toHaveBeenCalled()
    expect(container.textContent).toContain(message)
  })

  it('NipLookupField shows the server message on 503 NIP-LOOKUP-QUOTA', async () => {
    const message = 'Dzienny limit wyszukiwań w wykazie MF wyczerpany. Wpisz dane ręcznie.'
    const body = JSON.stringify({ errors: [{ code: 'NIP-LOOKUP-QUOTA', field: 'nip', message }], warnings: [] })
    const response = new Response(body, { status: 503, headers: { 'Content-Type': 'application/json' } })
    vi.mocked(lookupNip).mockRejectedValue(new ApiError('Serwer jest chwilowo niedostępny.', 503, response))
    await render(<NipLookupField id="nip" value="4444444444" onChange={() => {}} onFound={() => {}} />)
    await act(async () => container.querySelector('button')!.click())
    expect(container.textContent).toContain(message)
    expect(container.textContent).not.toContain('Biała lista nie odpowiada')
    expect(container.querySelector<HTMLInputElement>('#nip')!.readOnly).toBe(false)
    expect(container.querySelector('button')!.disabled).toBe(false)
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
