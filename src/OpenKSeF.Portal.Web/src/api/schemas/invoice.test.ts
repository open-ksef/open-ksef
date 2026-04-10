import { describe, expect, it } from 'vitest'

import {
  buyerKindSchema,
  createFinalInvoiceFromAdvancesRequestSchema,
  createInvoiceRequestSchema,
  currencyCodeSchema,
  ibanSchema,
  invoicePrintModelSchema,
  invoiceReadDtoSchema,
  nipSchema,
  updateInvoiceDraftRequestSchema,
  validationEnvelopeSchema,
} from './invoice'
import { getInvoiceRuleFamily, invoiceRuleCodeRegistry } from './ruleCodes'

describe('invoice schemas', () => {
  it('accepts canonical enum values', () => {
    expect(buyerKindSchema.parse('Business')).toBe('Business')
    expect(() => buyerKindSchema.parse('B2B')).toThrow()
  })

  it('validates NIP checksum and currency format', () => {
    expect(nipSchema.parse('1234563218')).toBe('1234563218')
    expect(() => nipSchema.parse('1234567890')).toThrow()
    expect(currencyCodeSchema.parse('PLN')).toBe('PLN')
    expect(() => currencyCodeSchema.parse('pln')).toThrow()
  })

  it('validates IBAN format and checksum', () => {
    expect(ibanSchema.parse('PL61 1090 1014 0000 0712 1981 2874')).toBe('PL61109010140000071219812874')
    expect(() => ibanSchema.parse('PL00109010140000071219812874')).toThrow()
  })

  it('parses create invoice and final-from-advances requests', () => {
    const createRequest = createInvoiceRequestSchema.parse({
      kind: 'VatInvoice',
      sellerName: 'Seller',
      sellerNip: '1234563218',
      buyerName: 'Buyer',
      buyerKind: 'Business',
      buyerNip: '8567346215',
      currency: 'PLN',
      issueDate: '2026-04-10',
      ksefSubmissionRequirement: 'Required',
    })

    const finalRequest = createFinalInvoiceFromAdvancesRequestSchema.parse({
      issueDate: '2026-04-12',
      advances: [
        {
          advanceInvoiceId: '11111111-1111-4111-8111-111111111111',
          advanceDocumentNumber: 'ADV/1',
          settledAmount: 50,
        },
      ],
    })

    expect(createRequest.kind).toBe('VatInvoice')
    expect(finalRequest.advances[0].advanceDocumentNumber).toBe('ADV/1')
  })

  it('parses invoice read, print, and validation envelope responses', () => {
    const invoice = invoiceReadDtoSchema.parse({
      id: '11111111-1111-4111-8111-111111111111',
      tenantId: '22222222-2222-4222-8222-222222222222',
      kind: 'VatInvoice',
      status: 'AcceptedByKsef',
      buyerKind: 'Business',
      ksefSubmissionRequirement: 'Required',
      ksefSubmissionState: 'Accepted',
      seller: { name: 'Seller', nip: '1234563218' },
      buyer: { name: 'Buyer', nip: '8567346215' },
      issueDate: '2026-04-10T00:00:00Z',
      saleDate: null,
      dueDate: null,
      approvedAt: '2026-04-10T10:00:00Z',
      submittedToKsefAt: '2026-04-10T11:00:00Z',
      acceptedByKsefAt: '2026-04-10T12:00:00Z',
      currency: 'PLN',
      totalNet: { amount: 100, currency: 'PLN' },
      totalVat: { amount: 23, currency: 'PLN' },
      totalGross: { amount: 123, currency: 'PLN' },
      documentNumber: 'FV/1',
      externalReference: null,
      paymentMethod: null,
      publicNotes: null,
      ksefDocumentNumber: 'KSEF-1',
      ksefReferenceNumber: 'REF-1',
      ksefRejectionReason: null,
      correctionReference: null,
      lines: [
        {
          lineNumber: 1,
          description: 'Service',
          quantity: 1,
          unitOfMeasure: 'pcs',
          pricingMode: 'Net',
          unitPrice: { amount: 100, currency: 'PLN' },
          discountPercent: null,
          vatRate: '23%',
          netAmount: { amount: 100, currency: 'PLN' },
          vatAmount: { amount: 23, currency: 'PLN' },
          grossAmount: { amount: 123, currency: 'PLN' },
          correctionRole: null,
        },
      ],
      advanceDocumentIds: [],
      settledAdvanceAllocations: [],
      duplicateIssuances: [],
    })

    const printModel = invoicePrintModelSchema.parse({
      invoiceData: invoice,
      variant: 'English',
      labels: {
        invoiceTitle: 'VAT INVOICE',
        sellerLabel: 'Seller',
        buyerLabel: 'Buyer',
        issueDateLabel: 'Issue Date',
        saleDateLabel: 'Sale Date',
        dueDateLabel: 'Due Date',
        documentNumberLabel: 'Document Number',
        currencyLabel: 'Currency',
        totalNetLabel: 'Total Net',
        totalVatLabel: 'Total VAT',
        totalGrossLabel: 'Total Gross',
        lineDescriptionLabel: 'Description',
        lineQuantityLabel: 'Quantity',
        lineUnitPriceLabel: 'Unit Price',
        lineNetAmountLabel: 'Net',
        lineVatRateLabel: 'VAT Rate',
        lineVatAmountLabel: 'VAT',
        lineGrossAmountLabel: 'Gross',
        duplicateLabel: 'DUPLICATE',
      },
      duplicateInfo: null,
    })

    const envelope = validationEnvelopeSchema.parse({
      stage: 'Approve',
      messages: [
        {
          code: 'INV-VAL-013',
          severity: 'Error',
          field: 'Buyer.Nip',
          messagePl: 'Dla nabywcy B2B wymagany jest poprawny NIP.',
          messageTechnical: 'BuyerKind=Business but BuyerSnapshot.Nip missing/invalid.',
        },
      ],
    })

    expect(printModel.variant).toBe('English')
    expect(envelope.messages[0].code).toBe('INV-VAL-013')
  })
})

describe('client-side rule mirroring', () => {
  const validBase = {
    kind: 'VatInvoice' as const,
    sellerName: 'Sprzedawca Sp. z o.o.',
    sellerNip: '1234563218',
    buyerName: 'Nabywca Sp. z o.o.',
    buyerKind: 'Business' as const,
    buyerNip: '8567346215',
    currency: 'PLN',
    issueDate: '2026-04-10',
    ksefSubmissionRequirement: 'Required' as const,
  }

  it('INV-VAL-001: rejects unsupported DocumentKind', () => {
    expect(() => createInvoiceRequestSchema.parse({ ...validBase, kind: 'Unknown' })).toThrow()
  })

  it('INV-VAL-003: rejects proforma with KSeF Required or Optional', () => {
    expect(() =>
      createInvoiceRequestSchema.parse({ ...validBase, kind: 'Proforma', ksefSubmissionRequirement: 'Required' }),
    ).toThrow()
    expect(() =>
      createInvoiceRequestSchema.parse({ ...validBase, kind: 'Proforma', ksefSubmissionRequirement: 'Optional' }),
    ).toThrow()
    // Proforma with Forbidden or NotApplicable is allowed
    expect(() =>
      createInvoiceRequestSchema.parse({
        ...validBase,
        kind: 'Proforma',
        ksefSubmissionRequirement: 'Forbidden',
        buyerKind: 'Consumer',
        buyerNip: null,
      }),
    ).not.toThrow()
    expect(() =>
      createInvoiceRequestSchema.parse({
        ...validBase,
        kind: 'Proforma',
        ksefSubmissionRequirement: 'NotApplicable',
        buyerKind: 'Consumer',
        buyerNip: null,
      }),
    ).not.toThrow()
  })

  it('INV-VAL-013: rejects Business buyer without NIP', () => {
    expect(() =>
      createInvoiceRequestSchema.parse({ ...validBase, buyerKind: 'Business', buyerNip: null }),
    ).toThrow()
    // Consumer without NIP is allowed
    expect(() =>
      createInvoiceRequestSchema.parse({ ...validBase, buyerKind: 'Consumer', buyerNip: null }),
    ).not.toThrow()
    // Unknown without NIP is a Warning only, not blocked
    expect(() =>
      createInvoiceRequestSchema.parse({
        ...validBase,
        buyerKind: 'Unknown',
        buyerNip: null,
        ksefSubmissionRequirement: 'Optional',
      }),
    ).not.toThrow()
  })

  it('INV-VAL-021: flags due date earlier than issue date as a warning-tagged issue', () => {
    const result = updateInvoiceDraftRequestSchema.safeParse({
      issueDate: '2026-04-10',
      dueDate: '2026-04-09',
    })
    expect(result.success).toBe(false)
    expect(result.error?.issues[0].params?.ruleCode).toBe('INV-VAL-021')
    expect(result.error?.issues[0].params?.severity).toBe('Warning')
  })

  it('INV-VAL-021: passes when due date equals or follows issue date', () => {
    expect(
      updateInvoiceDraftRequestSchema.safeParse({ issueDate: '2026-04-10', dueDate: '2026-04-10' }).success,
    ).toBe(true)
    expect(
      updateInvoiceDraftRequestSchema.safeParse({ issueDate: '2026-04-10', dueDate: '2026-04-20' }).success,
    ).toBe(true)
  })

  it('INV-VAL-050: rejects line with empty description', () => {
    const result = updateInvoiceDraftRequestSchema.safeParse({
      lines: [{ lineNumber: 1, description: '', quantity: 1, pricingMode: 'Net', unitPrice: 100, vatRate: '23%' }],
    })
    expect(result.success).toBe(false)
  })

  it('INV-VAL-051: rejects line with quantity <= 0', () => {
    const result = updateInvoiceDraftRequestSchema.safeParse({
      lines: [{ lineNumber: 1, description: 'Usługa', quantity: 0, pricingMode: 'Net', unitPrice: 100, vatRate: '23%' }],
    })
    expect(result.success).toBe(false)
  })
})

describe('invoice rule code registry', () => {
  it('maps known codes to families without localized text', () => {
    expect(getInvoiceRuleFamily('INV-VAL-013')).toBe('parties')
    expect(getInvoiceRuleFamily('INV-VAL-100')).toBe('state')
    expect(getInvoiceRuleFamily('INV-VAL-111')).toBe('ksef')
    expect(getInvoiceRuleFamily('INV-VAL-999')).toBeNull()
    expect(Object.keys(invoiceRuleCodeRegistry)).toContain('INV-VAL-060')
  })
})
