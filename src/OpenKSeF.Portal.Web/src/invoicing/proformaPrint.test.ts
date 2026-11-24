import { describe, expect, it } from 'vitest'

import type { IssuedInvoiceResponse } from '@/api/types'
import { buildProformaDocument, PROFORMA_FOOTER } from './proformaPrint'

const proforma = (patch: Partial<IssuedInvoiceResponse> = {}): IssuedInvoiceResponse =>
  ({
    id: 'p-1',
    status: 'Issued',
    invoiceType: 'PRO',
    number: 'PRO/1/11/2026',
    seller: { nip: '4444444444', name: 'Sprzedawca', addressLine1: 'ul. Prosta 1', addressLine2: '00-001 Warszawa', bankAccount: '93000000002026111700000815', cashAccounting: false },
    buyer: { kind: 'Company', nip: '7740001454', name: 'Nabywca', addressLine1: 'ul. Krzywa 2', addressLine2: '00-002 Kraków' },
    issueDate: '2026-11-24',
    saleDate: '2026-11-30',
    dueDate: '2026-12-08',
    paymentMethod: 'Transfer',
    splitPayment: true,
    exemptionBasis: null,
    lines: [{ lineNo: 1, name: 'Usługa', unit: 'szt.', quantity: 2, unitNetPrice: 500, vatRate: '23', netAmount: 1000, vatAmount: 230 }],
    vatSummary: [{ vatRate: '23', net: 1000, vat: 230 }],
    totalNet: 1000,
    totalVat: 230,
    totalGross: 1230,
    currency: 'PLN',
    ksef: { environment: 'test', ksefNumber: null, verificationUrl: null },
    ...patch,
  }) as IssuedInvoiceResponse

function texts(node: unknown, out: string[] = []): string[] {
  if (typeof node === 'string') out.push(node.replace(/\s/g, ' '))
  else if (Array.isArray(node)) node.forEach((item) => texts(item, out))
  else if (node && typeof node === 'object') {
    const record = node as Record<string, unknown>
    if (typeof record.text === 'string') out.push(record.text.replace(/\s/g, ' '))
    for (const key of ['stack', 'columns', 'table', 'body', 'content', 'footer']) texts(record[key], out)
  }
  return out
}

describe('proforma PDF', () => {
  it('REQ-0029 rule 8: title, number, footer, bank account and transfer title; no KSeF, KOD I, MPP or sale date', () => {
    const all = texts(buildProformaDocument(proforma()))
    const joined = all.join('\n')

    expect(all).toContain('Faktura proforma PRO/1/11/2026')
    expect(all).toContain(PROFORMA_FOOTER)
    expect(PROFORMA_FOOTER).toBe('Dokument nie jest fakturą VAT w rozumieniu ustawy o VAT i nie stanowi podstawy do odliczenia podatku.')
    expect(all).toContain('93 0000 0000 2026 1117 0000 0815')
    expect(all).toContain('Tytuł przelewu')
    expect(all).toContain('NIP 7740001454')
    expect(all).toContain('08.12.2026')
    expect(joined).not.toMatch(/KSeF|KOD I|podzielonej|Data sprzedaży|30\.11\.2026/)
  })

  it('a foreign-currency proforma shows totals in that currency only, without an exchange rate', () => {
    const joined = texts(buildProformaDocument(proforma({ currency: 'EUR', exchangeRate: null }))).join('\n')

    expect(joined).toContain('1 230,00 EUR')
    expect(joined).not.toMatch(/PLN|Kurs/)
  })

  it('a cash proforma has no account or transfer title', () => {
    const joined = texts(buildProformaDocument(proforma({ paymentMethod: 'Cash' }))).join('\n')

    expect(joined).toContain('Gotówka')
    expect(joined).not.toContain('Tytuł przelewu')
    expect(joined).not.toContain('Numer rachunku')
  })
})
