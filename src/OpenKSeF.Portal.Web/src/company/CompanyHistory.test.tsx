// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { listHistory } from '@/api/endpoints/tenants'
import type { TenantHistoryEventResponse } from '@/api/types'
import { CompanyHistory } from './CompanyHistory'
import { historyDetails, historyLabel } from './history'

vi.mock('@/api/endpoints/tenants', () => ({ listHistory: vi.fn() }))

const event = (action: string, fields: Partial<TenantHistoryEventResponse> = {}): TenantHistoryEventResponse => ({
  id: crypto.randomUUID(),
  occurredAt: '2026-11-20T10:15:00Z',
  action,
  actor: 'Anna Nowak',
  actorDeleted: false,
  targetName: null,
  targetEmail: null,
  target: null,
  ...fields,
})

const cases: [string, Partial<TenantHistoryEventResponse>, string, string][] = [
  ['invoice.paid', { target: 'FV/1/2026' }, 'Oznaczono fakturę jako opłaconą', 'FV/1/2026'],
  ['invoice.unpaid', { target: 'FV/2/2026' }, 'Oznaczono fakturę jako nieopłaconą', 'FV/2/2026'],
  ['invoices.exported', { target: 'od=2026-10-01;do=2026-10-31;kierunki=sprzedaz,zakup;sprzedaz=12;zakup=30' }, 'Pobrano eksport faktur', 'okres 2026-10-01 – 2026-10-31, sprzedaż: 12, zakup: 30'],
  ['credential.set', { target: 'Certificate' }, 'Zapisano dane dostępowe KSeF', 'certyfikat'],
  ['credential.deleted', {}, 'Usunięto dane dostępowe KSeF', ''],
  ['invite.created', { targetEmail: 'jan@firma.pl', target: 'Accountant' }, 'Wysłano zaproszenie', 'jan@firma.pl, Księgowy'],
  ['invite.resent', { targetEmail: 'jan@firma.pl', target: 'Employee' }, 'Ponownie wysłano zaproszenie', 'jan@firma.pl, Pracownik'],
  ['invite.revoked', { targetEmail: 'jan@firma.pl', target: 'Administrator' }, 'Odwołano zaproszenie', 'jan@firma.pl, Administrator'],
  ['invite.accepted', { targetName: 'Jan Kowalski', targetEmail: 'jan@firma.pl', target: 'Accountant' }, 'Przyjęto zaproszenie', 'jan@firma.pl, Księgowy'],
  ['access.granted', { targetName: 'Jan Kowalski', targetEmail: 'jan@firma.pl', target: 'Employee' }, 'Nadano dostęp', 'Jan Kowalski, Pracownik'],
  ['member.roleChanged', { targetName: 'Jan Kowalski', target: 'Accountant->Administrator' }, 'Zmieniono rolę', 'Jan Kowalski, Księgowy → Administrator'],
  ['member.removed', { targetEmail: 'jan@firma.pl', target: 'Owner' }, 'Usunięto osobę z firmy', 'jan@firma.pl, Właściciel'],
  ['member.left', { targetName: 'Jan Kowalski', target: 'Employee' }, 'Osoba opuściła firmę', 'Pracownik'],
  ['tenant.notificationEmailCleared', { targetEmail: 'biuro@firma.pl' }, 'Usunięto adres e-mail do powiadomień', 'biuro@firma.pl'],
  ['tenant.ownershipClaimed', {}, 'Przejęto firmę tokenem KSeF', ''],
]

describe('Historia zmian, etykiety i szczegóły', () => {
  it.each(cases)('%s', (action, fields, label, details) => {
    expect(historyLabel(action)).toBe(label)
    expect(historyDetails(event(action, fields))).toBe(details)
  })

  it('shows a dash for a paid invoice that no longer exists', () => {
    expect(historyDetails(event('invoice.paid'))).toBe('—')
  })

  it('says cały okres and lists only exported directions', () => {
    expect(historyDetails(event('invoices.exported', { target: 'od=;do=;kierunki=zakup;sprzedaz=0;zakup=7' }))).toBe('cały okres, zakup: 7')
  })

  it('shows an unknown code as is with no details', () => {
    expect(historyLabel('sync.started')).toBe('sync.started')
    expect(historyDetails(event('sync.started', { target: 'x' }))).toBe('')
  })
})

describe('Historia zmian, lista', () => {
  let root: Root
  let container: HTMLDivElement
  const settle = async () => {
    for (let tick = 0; tick < 8; tick++) await act(() => new Promise<void>((resolve) => setTimeout(resolve, 0)))
  }
  const rows = () => [...container.querySelectorAll('tbody tr')].map((row) => [...row.querySelectorAll('td')].map((cell) => cell.textContent))
  const more = () => container.querySelector<HTMLElement>('[data-testid="company-history-more"]')
  const page = (count: number, start: number) => Array.from({ length: count }, (_, index) => event('credential.deleted', { id: `e${start + index}` }))

  beforeEach(() => {
    vi.clearAllMocks()
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
    container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
  })

  afterEach(() => {
    act(() => root.unmount())
    container.remove()
  })

  async function render(): Promise<void> {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    await act(async () =>
      root.render(
        <QueryClientProvider client={client}>
          <CompanyHistory tenantId="a" />
        </QueryClientProvider>,
      ),
    )
    await settle()
  }

  it('renders Data, Kto, Zdarzenie, Szczegóły and Usunięte konto for an anonymised actor', async () => {
    vi.mocked(listHistory).mockResolvedValue({
      items: [event('member.roleChanged', { actor: null, actorDeleted: true, targetName: 'Jan Kowalski', target: 'Accountant->Administrator' })],
      hasMore: false,
    })
    await render()

    expect([...container.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual(['Data', 'Kto', 'Zdarzenie', 'Szczegóły'])
    const [row] = rows()
    expect(row[0]).toMatch(/^\d{2}\.\d{2}\.2026,? \d{2}:\d{2}$/)
    expect(row.slice(1)).toEqual(['Usunięte konto', 'Zmieniono rolę', 'Jan Kowalski, Księgowy → Administrator'])
    expect(more()).toBeNull()
  })

  it('loads older pages with Pokaż starsze until there are no more', async () => {
    vi.mocked(listHistory)
      .mockResolvedValueOnce({ items: page(50, 0), hasMore: true })
      .mockResolvedValueOnce({ items: page(50, 50), hasMore: true })
      .mockResolvedValueOnce({ items: page(20, 100), hasMore: false })
    await render()
    expect(rows()).toHaveLength(50)
    expect(more()?.textContent).toBe('Pokaż starsze')

    await act(async () => more()?.click())
    await settle()
    expect(listHistory).toHaveBeenLastCalledWith('a', 'e49')
    expect(rows()).toHaveLength(100)

    await act(async () => more()?.click())
    await settle()
    expect(listHistory).toHaveBeenLastCalledWith('a', 'e99')
    expect(rows()).toHaveLength(120)
    expect(more()).toBeNull()
  })
})
