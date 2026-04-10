import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ZodError } from 'zod'

import { ApiError } from './errors'

const apiClientMock = {
  get: vi.fn(),
  post: vi.fn(),
  patch: vi.fn(),
}

vi.mock('./client', () => ({
  apiClient: apiClientMock,
}))

describe('invoicesAggregateApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls aggregate list, detail, print, and duplicates endpoints with typed parsing', async () => {
    const invoice = createInvoiceReadDto()
    apiClientMock.get
      .mockResolvedValueOnce({
        items: [invoice],
        page: 2,
        pageSize: 50,
        totalCount: 1,
        totalPages: 1,
      })
      .mockResolvedValueOnce(invoice)
      .mockResolvedValueOnce({
        invoiceData: invoice,
        variant: 'English',
        labels: createPrintLabels(),
        duplicateInfo: null,
      })
      .mockResolvedValueOnce([
        {
          issuedAt: '2026-04-10T12:00:00Z',
          issuedBy: 'user-1',
          originalInvoiceId: invoice.id,
          originalDocumentNumber: 'FV/1',
        },
      ])

    const {
      getAggregateInvoice,
      getInvoicePrint,
      listAggregateInvoices,
      listInvoiceDuplicates,
    } = await import('./invoicesAggregateApi')

    const listed = await listAggregateInvoices('tenant-1', {
      status: ['Draft', 'Approved'],
      kind: ['AdvanceInvoice'],
      buyerKind: 'Business',
      from: '2026-01-01',
      to: '2026-01-31',
      page: 2,
      pageSize: 50,
    })
    const detail = await getAggregateInvoice('tenant-1', invoice.id)
    const print = await getInvoicePrint('tenant-1', invoice.id, 'English')
    const duplicates = await listInvoiceDuplicates('tenant-1', invoice.id)

    expect(apiClientMock.get).toHaveBeenNthCalledWith(
      1,
      '/tenants/tenant-1/invoices/aggregate?status=Draft&status=Approved&kind=AdvanceInvoice&buyerKind=Business&from=2026-01-01&to=2026-01-31&page=2&pageSize=50',
    )
    expect(apiClientMock.get).toHaveBeenNthCalledWith(2, `/tenants/tenant-1/invoices/aggregate/${invoice.id}`)
    expect(apiClientMock.get).toHaveBeenNthCalledWith(
      3,
      `/tenants/tenant-1/invoices/aggregate/${invoice.id}/print?variant=English`,
    )
    expect(apiClientMock.get).toHaveBeenNthCalledWith(
      4,
      `/tenants/tenant-1/invoices/aggregate/${invoice.id}/duplicates`,
    )
    expect(listed.items[0].status).toBe('AcceptedByKsef')
    expect(detail.documentNumber).toBe('FV/1')
    expect(print.variant).toBe('English')
    expect(duplicates).toHaveLength(1)
  })

  it('calls aggregate mutation endpoints and returns parsed invoices', async () => {
    const created = createInvoiceReadDto({ status: 'Draft' })
    const approved = createInvoiceReadDto({ status: 'Approved' })
    const submitted = createInvoiceReadDto({ status: 'SubmittedToKsef', ksefSubmissionState: 'Submitted' })
    const accepted = createInvoiceReadDto({ status: 'AcceptedByKsef', ksefSubmissionState: 'Accepted' })
    const rejected = createInvoiceReadDto({ status: 'RejectedByKsef', ksefSubmissionState: 'Rejected' })
    const correction = createInvoiceReadDto({
      id: '33333333-3333-4333-8333-333333333333',
      status: 'Draft',
      correctionReference: {
        originalInvoiceId: created.id,
        originalDocumentNumber: 'FV/1',
        reasonKind: 'Formal',
        reasonDescription: 'Fix buyer note',
      },
    })
    const finalInvoice = createInvoiceReadDto({
      id: '44444444-4444-4444-8444-444444444444',
      kind: 'FinalInvoice',
      status: 'Draft',
    })

    apiClientMock.post
      .mockResolvedValueOnce(created)
      .mockResolvedValueOnce(approved)
      .mockResolvedValueOnce(approved)
      .mockResolvedValueOnce(submitted)
      .mockResolvedValueOnce(accepted)
      .mockResolvedValueOnce(rejected)
      .mockResolvedValueOnce(correction)
      .mockResolvedValueOnce(finalInvoice)
    apiClientMock.patch.mockResolvedValueOnce(created)

    const {
      approveInvoice,
      createCorrectionFromOriginal,
      createFinalInvoiceFromAdvances,
      createInvoiceDraft,
      recordKsefAcceptance,
      recordKsefRejection,
      reopenInvoice,
      submitInvoiceToKsef,
      updateInvoiceDraft,
    } = await import('./invoicesAggregateApi')

    const createRequest = {
      kind: 'VatInvoice',
      sellerName: 'Seller',
      sellerNip: '1234563218',
      buyerName: 'Buyer',
      buyerKind: 'Business',
      buyerNip: '8567346215',
      currency: 'PLN',
      issueDate: '2026-04-10',
      ksefSubmissionRequirement: 'Required',
    } as const

    await expect(createInvoiceDraft('tenant-1', createRequest)).resolves.toEqual(created)
    await expect(
      updateInvoiceDraft('tenant-1', created.id, {
        issueDate: '2026-04-11',
        documentNumber: 'FV/1',
      }),
    ).resolves.toEqual(created)
    await expect(approveInvoice('tenant-1', created.id)).resolves.toEqual(approved)
    await expect(reopenInvoice('tenant-1', created.id)).resolves.toEqual(approved)
    await expect(submitInvoiceToKsef('tenant-1', created.id)).resolves.toEqual(submitted)
    await expect(
      recordKsefAcceptance('tenant-1', created.id, {
        ksefDocumentNumber: 'KSEF-1',
        ksefReferenceNumber: 'REF-1',
        acceptedAt: '2026-04-10T12:00:00Z',
      }),
    ).resolves.toEqual(accepted)
    await expect(
      recordKsefRejection('tenant-1', created.id, {
        rejectionReason: 'Gateway rejected the payload.',
        rejectedAt: '2026-04-10T12:00:00Z',
      }),
    ).resolves.toEqual(rejected)
    await expect(
      createCorrectionFromOriginal('tenant-1', created.id, {
        issueDate: '2026-04-12',
        reasonKind: 'Formal',
        reasonDescription: 'Fix buyer note',
      }),
    ).resolves.toEqual(correction)
    await expect(
      createFinalInvoiceFromAdvances('tenant-1', {
        issueDate: '2026-04-13',
        advances: [
          {
            advanceInvoiceId: '11111111-1111-4111-8111-111111111111',
            advanceDocumentNumber: 'ADV/1',
            settledAmount: 50,
          },
        ],
      }),
    ).resolves.toEqual(finalInvoice)

    expect(apiClientMock.post).toHaveBeenNthCalledWith(1, '/tenants/tenant-1/invoices/aggregate', createRequest)
    expect(apiClientMock.patch).toHaveBeenCalledWith(
      `/tenants/tenant-1/invoices/aggregate/${created.id}/draft`,
      { issueDate: '2026-04-11', documentNumber: 'FV/1' },
    )
    expect(apiClientMock.post).toHaveBeenNthCalledWith(2, `/tenants/tenant-1/invoices/aggregate/${created.id}/approve`, {})
    expect(apiClientMock.post).toHaveBeenNthCalledWith(3, `/tenants/tenant-1/invoices/aggregate/${created.id}/reopen`, {})
    expect(apiClientMock.post).toHaveBeenNthCalledWith(
      4,
      `/tenants/tenant-1/invoices/aggregate/${created.id}/submit-to-ksef`,
    )
    expect(apiClientMock.post).toHaveBeenNthCalledWith(
      5,
      `/tenants/tenant-1/invoices/aggregate/${created.id}/ksef-acceptance`,
      {
        ksefDocumentNumber: 'KSEF-1',
        ksefReferenceNumber: 'REF-1',
        acceptedAt: '2026-04-10T12:00:00Z',
      },
    )
    expect(apiClientMock.post).toHaveBeenNthCalledWith(
      6,
      `/tenants/tenant-1/invoices/aggregate/${created.id}/ksef-rejection`,
      {
        rejectionReason: 'Gateway rejected the payload.',
        rejectedAt: '2026-04-10T12:00:00Z',
      },
    )
    expect(apiClientMock.post).toHaveBeenNthCalledWith(
      7,
      `/tenants/tenant-1/invoices/aggregate/${created.id}/corrections`,
      {
        issueDate: '2026-04-12',
        reasonKind: 'Formal',
        reasonDescription: 'Fix buyer note',
      },
    )
    expect(apiClientMock.post).toHaveBeenNthCalledWith(8, '/tenants/tenant-1/invoices/aggregate/final-from-advances', {
      issueDate: '2026-04-13',
      advances: [
        {
          advanceInvoiceId: '11111111-1111-4111-8111-111111111111',
          advanceDocumentNumber: 'ADV/1',
          settledAmount: 50,
        },
      ],
    })
  })

  it('throws InvoiceValidationError for validation envelope responses', async () => {
    apiClientMock.post.mockRejectedValueOnce(
      new ApiError(
        'Conflict',
        409,
        new Response(
          JSON.stringify({
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
          }),
          {
            status: 409,
            headers: { 'Content-Type': 'application/json' },
          },
        ),
      ),
    )

    const { InvoiceValidationError, approveInvoice } = await import('./invoicesAggregateApi')

    const error = await approveInvoice('tenant-1', '11111111-1111-4111-8111-111111111111').catch(
      (caughtError: unknown) => caughtError,
    )

    expect(error).toBeInstanceOf(InvoiceValidationError)
    expect(error).toMatchObject({
      name: 'InvoiceValidationError',
      stage: 'Approve',
      status: 409,
      messages: [
        expect.objectContaining({
          code: 'INV-VAL-013',
          field: 'Buyer.Nip',
        }),
      ],
    })
  })

  it('throws when response shape drifts from the schema', async () => {
    apiClientMock.get.mockResolvedValueOnce({
      ...createInvoiceReadDto(),
      status: 'Archived',
    })

    const { getAggregateInvoice } = await import('./invoicesAggregateApi')

    await expect(getAggregateInvoice('tenant-1', '11111111-1111-4111-8111-111111111111')).rejects.toBeInstanceOf(
      ZodError,
    )
  })
})

function createInvoiceReadDto(overrides: Record<string, unknown> = {}) {
  return {
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
    internalNotes: null,
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
    ...overrides,
  }
}

function createPrintLabels() {
  return {
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
  }
}
