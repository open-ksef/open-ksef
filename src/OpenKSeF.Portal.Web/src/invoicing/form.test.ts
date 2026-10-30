import { describe, expect, it } from 'vitest'

import type { IssuedInvoiceResponse } from '@/api/types'
import { ART_113, emptyDraft, fromResponse, toDraftRequest, toFieldErrors } from './form'

describe('draft form mapping', () => {
  it('defaults: sale date = issue date, due date +14 days, one 23% line', () => {
    const draft = emptyDraft('2026-12-25')
    expect(draft.saleDate).toBe('2026-12-25')
    expect(draft.dueDate).toBe('2027-01-08')
    expect(draft.lines).toHaveLength(1)
    expect(draft.lines[0].vatRate).toBe('23')
  })

  it('sends dates unchanged, parses Polish decimals, drops NIP for a person', () => {
    const draft = emptyDraft('2026-11-01')
    draft.buyer = { kind: 'Person', nip: '5260250274', name: ' Jan Kowalski ', addressLine1: 'ul. A 1', addressLine2: '' }
    draft.lines = [{ name: 'Usługa', unit: 'h', quantity: '1,5', unitNetPrice: '1 200,50', vatRate: '8' }]

    const request = toDraftRequest(draft)

    expect(request.issueDate).toBe('2026-11-01')
    expect(request.saleDate).toBe('2026-11-01')
    expect(request.buyer).toEqual({ kind: 'Person', nip: null, name: 'Jan Kowalski', addressLine1: 'ul. A 1', addressLine2: null })
    expect(request.lines).toEqual([{ name: 'Usługa', unit: 'h', quantity: 1.5, unitNetPrice: 1200.5, vatRate: '8' }])
    expect(request.exemptionBasis).toBeNull()
  })

  it('sends the art. 113 preset or the typed basis only when a zw line exists', () => {
    const draft = emptyDraft('2026-11-01')
    draft.lines[0].vatRate = 'zw'
    expect(toDraftRequest(draft).exemptionBasis).toEqual({ kind: 'Act', text: ART_113 })

    draft.exemptionChoice = 'Directive'
    draft.exemptionText = 'art. 132 ust. 1 lit. i'
    expect(toDraftRequest(draft).exemptionBasis).toEqual({ kind: 'Directive', text: 'art. 132 ust. 1 lit. i' })
  })

  it('round-trips a saved draft', () => {
    const draft = emptyDraft('2026-11-01')
    draft.lines = [{ name: 'X', unit: 'szt.', quantity: '2', unitNetPrice: '10,25', vatRate: 'zw' }]
    draft.exemptionChoice = 'Other'
    draft.exemptionText = 'inna'
    const request = toDraftRequest(draft)
    const response = {
      ...request,
      buyer: request.buyer!,
      lines: request.lines!.map((line, index) => ({ ...line, lineNo: index + 1, netAmount: 0, vatAmount: 0 })),
    } as unknown as IssuedInvoiceResponse

    expect(fromResponse(response)).toEqual(draft)
  })

  it('maps envelope fields to the first message per field', () => {
    expect(
      toFieldErrors([
        { code: 'A', field: 'buyer.nip', message: 'first' },
        { code: 'B', field: 'buyer.nip', message: 'second' },
        { code: 'C', field: null, message: 'doc' },
      ]),
    ).toEqual({ 'buyer.nip': 'first' })
  })
})
