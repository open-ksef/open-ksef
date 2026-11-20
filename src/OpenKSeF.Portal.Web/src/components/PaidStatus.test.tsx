import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'

import type { InvoiceResponse } from '@/api/types'
import { PaidStatus } from './PaidStatus'

const text = (row: Partial<InvoiceResponse>): string =>
  renderToStaticMarkup(<PaidStatus row={row as InvoiceResponse} testId="s" />).replace(/<[^>]+>/g, '')

describe('PaidStatus', () => {
  it('names the date and who marked it paid', () => {
    expect(text({ isPaid: true, paidAt: '2026-03-04T10:00:00Z', paidByName: 'Jan Kowalski' })).toBe('Opłacona 04.03.2026 przez Jan Kowalski')
  })

  it('names the seller when the invoice XML said paid', () => {
    expect(text({ isPaid: true, paidAt: '2026-03-04T10:00:00Z', paidByName: null, paidBySeller: true })).toBe('Opłacona wg sprzedawcy')
  })

  it('stays short without a name, and for unpaid', () => {
    expect(text({ isPaid: true, paidAt: '2026-03-04T10:00:00Z', paidByName: null })).toBe('Opłacona')
    expect(text({ isPaid: false, paidAt: null, paidByName: 'Jan Kowalski' })).toBe('Nieopłacona')
  })
})
