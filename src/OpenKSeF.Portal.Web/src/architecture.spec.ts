/**
 * X4 Architectural guard: draft/approve/reopen/submit/correction/final-from-advances
 * mutation flows must call only the aggregate API, never the legacy InvoicesController.
 * The only legacy invoice mutations allowed are setInvoicePaid (synced detail page).
 *
 * X5 Accessibility baseline: every portal status badge defines non-color
 * differentiators (icon string + label text) for every DocumentStatus value.
 *
 * X7 Shape-drift detection: Zod response schemas throw ZodError for unknown enum
 * values so that backend/frontend contract drift fails loudly at parse time.
 */
import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { documentKindSchema, documentStatusSchema, ksefSubmissionStateSchema, invoiceReadDtoSchema } from '@/api/schemas/invoice'

const projectRoot = resolve(import.meta.dirname, '..')
const pagesDir = resolve(projectRoot, 'src', 'pages')

function readPage(name: string): string {
  return readFileSync(resolve(pagesDir, name), 'utf8')
}

/** Pages that implement draft/approve/reopen/submit/correction/final mutation flows. */
const mutationPages = [
  'InvoiceDraftCreate.tsx',
  'InvoiceDraftEdit.tsx',
  'InvoiceApproveReview.tsx',
  'InvoiceKsefSubmit.tsx',
  'InvoiceCorrectionCreate.tsx',
  'InvoiceFinalFromAdvances.tsx',
]

describe('Portal architectural guard (X4)', () => {
  it('mutation pages import only from invoicesAggregateApi, not legacy invoices endpoint', () => {
    const violations: string[] = []

    for (const page of mutationPages) {
      const src = readPage(page)
      if (src.includes("from '@/api/endpoints/invoices'")) {
        violations.push(page)
      }
    }

    expect(violations).toEqual(
      [],
      `Mutation pages must not import from legacy @/api/endpoints/invoices. Violations: ${violations.join(', ')}`,
    )
  })

  it('aggregate detail page uses aggregate API for reopen', () => {
    const src = readPage('InvoiceAggregateDetail.tsx')
    expect(src).toContain("from '@/api/invoicesAggregateApi'")
    expect(src).not.toContain("from '@/api/endpoints/invoices'")
  })

  it('synced invoice detail page is the only page allowed to call setInvoicePaid', () => {
    const paidCallers: string[] = []
    const allPages = [
      ...mutationPages,
      'InvoiceAggregateDetail.tsx',
      'InvoiceList.tsx',
      'InvoicePrintView.tsx',
    ]

    for (const page of allPages) {
      const src = readPage(page)
      if (src.includes('setInvoicePaid')) {
        paidCallers.push(page)
      }
    }

    expect(paidCallers).toEqual([])
  })
})

describe('Portal accessibility baseline (X5)', () => {
  it('DocumentStatusBadge defines icon and label for every status value', () => {
    const src = readFileSync(
      resolve(projectRoot, 'src', 'components', 'invoices', 'DocumentStatusBadge.tsx'),
      'utf8',
    )

    const statuses = ['Draft', 'Approved', 'SubmittedToKsef', 'AcceptedByKsef', 'RejectedByKsef']
    for (const status of statuses) {
      expect(src, `${status} must appear in statusPresentation`).toContain(status)
    }

    // icon field is defined per entry
    expect(src).toContain('icon:')
    // label field is defined per entry
    expect(src).toContain('label:')
  })

  it('DocumentStatusBadge renders icon element and label span', () => {
    const src = readFileSync(
      resolve(projectRoot, 'src', 'components', 'invoices', 'DocumentStatusBadge.tsx'),
      'utf8',
    )

    // aria-hidden icon prevents screen-reader double-reading
    expect(src).toContain('aria-hidden')
    // aria-label on the container provides the accessible name
    expect(src).toContain('aria-label')
    // role="status" is present for live-region semantics
    expect(src).toContain('role="status"')
  })
})

describe('Shape-drift detection (X7)', () => {
  it('documentStatusSchema throws for unknown status value', () => {
    expect(() => documentStatusSchema.parse('Archived')).toThrow()
    expect(() => documentStatusSchema.parse('unknown')).toThrow()
    expect(() => documentStatusSchema.parse(null)).toThrow()
  })

  it('documentKindSchema throws for unknown kind value', () => {
    expect(() => documentKindSchema.parse('CreditNote')).toThrow()
    expect(() => documentKindSchema.parse('')).toThrow()
  })

  it('ksefSubmissionStateSchema throws for unknown state value', () => {
    expect(() => ksefSubmissionStateSchema.parse('Pending')).toThrow()
    expect(() => ksefSubmissionStateSchema.parse(42)).toThrow()
  })

  it('invoiceReadDtoSchema throws ZodError when backend sends unknown status', () => {
    // Simulate a backend response that adds a new status value not yet known to frontend.
    // This should fail loudly so developers notice the drift immediately.
    const dtoWithUnknownStatus = {
      id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
      tenantId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
      kind: 'VatInvoice',
      status: 'Archived', // not in documentStatusSchema
      buyerKind: 'Business',
      ksefSubmissionRequirement: 'Required',
      ksefSubmissionState: 'NotPlanned',
      seller: { name: 'Seller', nip: null },
      buyer: { name: 'Buyer', nip: null },
      issueDate: '2026-01-01',
      saleDate: null,
      dueDate: null,
      approvedAt: null,
      submittedToKsefAt: null,
      acceptedByKsefAt: null,
      currency: 'PLN',
      totalNet: { amount: 100, currency: 'PLN' },
      totalVat: { amount: 23, currency: 'PLN' },
      totalGross: { amount: 123, currency: 'PLN' },
      documentNumber: null,
      externalReference: null,
      paymentMethod: null,
      publicNotes: null,
      ksefDocumentNumber: null,
      ksefReferenceNumber: null,
      ksefRejectionReason: null,
      reopenAllowed: false,
      correctionReference: null,
      lines: [],
      advanceDocumentIds: [],
      settledAdvanceAllocations: [],
      duplicateIssuances: [],
    }

    expect(() => invoiceReadDtoSchema.parse(dtoWithUnknownStatus)).toThrow()
  })
})
