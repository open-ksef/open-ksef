import { z } from 'zod'

export const documentKindSchema = z.enum([
  'VatInvoice',
  'AdvanceInvoice',
  'FinalInvoice',
  'Proforma',
  'CorrectionInvoice',
])

export const documentStatusSchema = z.enum([
  'Draft',
  'Approved',
  'SubmittedToKsef',
  'AcceptedByKsef',
  'RejectedByKsef',
])

export const buyerKindSchema = z.enum(['Business', 'Consumer', 'Unknown'])
export const pricingModeSchema = z.enum(['Net', 'Gross'])
export const ksefSubmissionRequirementSchema = z.enum(['Required', 'Optional', 'Forbidden', 'NotApplicable'])
export const ksefSubmissionStateSchema = z.enum(['NotPlanned', 'Ready', 'Submitted', 'Accepted', 'Rejected'])
export const correctionReasonKindSchema = z.enum([
  'Formal',
  'ValueChange',
  'QuantityChange',
  'VatChange',
  'BuyerDataChange',
  'Other',
])

export const printVariantSchema = z.enum(['Standard', 'Duplicate', 'English'])
export const validationSeveritySchema = z.enum(['Warning', 'Error'])
export const validationStageSchema = z.enum(['Draft', 'Approve', 'SendToKsef'])

export const isoDateSchema = z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'Expected ISO date (YYYY-MM-DD)')
export const currencyCodeSchema = z
  .string()
  .trim()
  .length(3, 'Expected ISO-4217 currency code')
  .regex(/^[A-Z]{3}$/, 'Expected ISO-4217 currency code')

export const nipSchema = z
  .string()
  .trim()
  .regex(/^\d{10}$/, 'Expected 10-digit NIP')
  .refine(isValidNip, 'Invalid NIP checksum')

export const ibanSchema = z
  .string()
  .trim()
  .transform((value) => value.replace(/\s+/g, '').toUpperCase())
  .pipe(z.string().regex(/^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$/, 'Expected IBAN'))
  .refine(isValidIban, 'Invalid IBAN checksum')

export const createInvoiceRequestSchema = z
  .object({
    /** mirrors INV-VAL-001: DocumentKind must be a supported v1 kind */
    kind: documentKindSchema,
    /** mirrors INV-VAL-010: seller legal name is required */
    sellerName: z.string().trim().min(1),
    /** mirrors INV-VAL-011: seller NIP is required */
    sellerNip: nipSchema,
    buyerName: z.string().trim().min(1),
    /**
     * mirrors INV-VAL-012 (Warning, Draft): 'Unknown' is accepted but the form should
     * render a warning when selected; not blocked here since it is advisory only.
     */
    buyerKind: buyerKindSchema,
    /**
     * mirrors INV-VAL-013: B2B buyer requires valid NIP.
     * Cross-field enforcement in superRefine below.
     */
    buyerNip: nipSchema.nullish(),
    /** mirrors INV-VAL-040: currency code is required (ISO-4217 format) */
    currency: currencyCodeSchema,
    /** mirrors INV-VAL-020: issue date is required */
    issueDate: isoDateSchema,
    /**
     * mirrors INV-VAL-003 (cross-field): proforma cannot be KSeF-submittable.
     * Cross-field enforcement in superRefine below.
     * Rules INV-VAL-090..093 (KSeF requirement resolution) are server-authoritative only.
     */
    ksefSubmissionRequirement: ksefSubmissionRequirementSchema,
    documentNumber: z.string().trim().min(1).nullish(),
    externalReference: z.string().trim().min(1).nullish(),
  })
  .superRefine((data, ctx) => {
    // INV-VAL-003: proforma cannot be marked as KSeF-submittable
    if (
      data.kind === 'Proforma' &&
      (data.ksefSubmissionRequirement === 'Required' || data.ksefSubmissionRequirement === 'Optional')
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['ksefSubmissionRequirement'],
        message: 'Proforma nie jest dokumentem fiskalnym i nie może zostać wysłana do KSeF.',
        params: { ruleCode: 'INV-VAL-003' },
      })
    }
    // INV-VAL-013: B2B buyer requires valid NIP
    if (data.buyerKind === 'Business' && !data.buyerNip) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['buyerNip'],
        message: 'Dla nabywcy B2B wymagany jest poprawny NIP.',
        params: { ruleCode: 'INV-VAL-013' },
      })
    }
  })

export const updateInvoiceDraftRequestSchema = z
  .object({
    /** mirrors INV-VAL-020: issue date is required at Approve/SendToKsef; format validated here */
    issueDate: isoDateSchema.optional(),
    /**
     * mirrors INV-VAL-022: sale date may be required per policy.
     * Format is validated here; presence requirement is server-authoritative (policy-dependent).
     */
    saleDate: isoDateSchema.nullish(),
    /**
     * mirrors INV-VAL-021 (Warning, Draft): due date must not be earlier than issue date.
     * Cross-field warning in superRefine below.
     */
    dueDate: isoDateSchema.nullish(),
    documentNumber: z.string().trim().min(1).nullish(),
    externalReference: z.string().trim().min(1).nullish(),
    paymentMethod: z.string().trim().min(1).nullish(),
    publicNotes: z.string().trim().min(1).nullish(),
    internalNotes: z.string().trim().min(1).nullish(),
    /**
     * mirrors INV-VAL-002: when lines are supplied the array cannot be empty (fiscal document
     * must contain at least one line; enforced at Approve stage server-side for existing drafts).
     * mirrors INV-VAL-050: each line description is required.
     * mirrors INV-VAL-051: each line quantity must be > 0.
     * INV-VAL-052 (line totals consistency) and INV-VAL-053 (document totals = sum of lines)
     * are server-authoritative — amounts are computed server-side and are not part of this payload.
     */
    lines: z
      .array(
        z.object({
          lineNumber: z.number().int().positive(),
          /** mirrors INV-VAL-050: line description is required */
          description: z.string().trim().min(1),
          /** mirrors INV-VAL-051: quantity must be > 0 for standard lines */
          quantity: z.number().finite().positive(),
          unitOfMeasure: z.string().trim().min(1).nullish(),
          pricingMode: pricingModeSchema,
          unitPrice: z.number().finite().nonnegative(),
          discountPercent: z.number().finite().nonnegative().nullish(),
          vatRate: z.string().trim().min(1),
        }),
      )
      .min(1)
      .optional(),
  })
  .superRefine((data, ctx) => {
    // INV-VAL-021 (Warning): due date earlier than issue date — non-blocking in the UI
    if (data.issueDate && data.dueDate && data.dueDate < data.issueDate) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        path: ['dueDate'],
        message: 'Termin płatności jest wcześniejszy niż data wystawienia.',
        params: { ruleCode: 'INV-VAL-021', severity: 'Warning' },
      })
    }
  })

export const createCorrectionFromOriginalRequestSchema = z.object({
  issueDate: isoDateSchema,
  reasonKind: correctionReasonKindSchema,
  reasonDescription: z.string().trim().min(1),
})

export const advanceSettlementEntryRequestSchema = z.object({
  advanceInvoiceId: z.string().uuid(),
  advanceDocumentNumber: z.string().trim().min(1),
  settledAmount: z.number().finite().nonnegative(),
})

export const createFinalInvoiceFromAdvancesRequestSchema = z.object({
  issueDate: isoDateSchema,
  advances: z.array(advanceSettlementEntryRequestSchema).min(1),
})

export const partyReadDtoSchema = z.object({
  name: z.string(),
  nip: z.string().nullable(),
})

export const moneyReadDtoSchema = z.object({
  amount: z.number().finite(),
  currency: z.string(),
})

export const invoiceLineReadDtoSchema = z.object({
  lineNumber: z.number().int(),
  description: z.string(),
  quantity: z.number().finite(),
  unitOfMeasure: z.string().nullable(),
  pricingMode: pricingModeSchema,
  unitPrice: moneyReadDtoSchema,
  discountPercent: z.number().finite().nullable(),
  vatRate: z.string(),
  netAmount: moneyReadDtoSchema,
  vatAmount: moneyReadDtoSchema,
  grossAmount: moneyReadDtoSchema,
  correctionRole: z.string().nullable(),
})

export const correctionReferenceReadDtoSchema = z.object({
  originalInvoiceId: z.string().uuid(),
  originalDocumentNumber: z.string(),
  reasonKind: correctionReasonKindSchema,
  reasonDescription: z.string().nullable(),
})

export const advanceAllocationReadDtoSchema = z.object({
  advanceInvoiceId: z.string().uuid(),
  advanceDocumentNumber: z.string(),
  settledAmount: moneyReadDtoSchema,
})

export const duplicateIssuanceReadDtoSchema = z.object({
  issuedAt: z.string(),
  issuedBy: z.string().nullable(),
})

export const invoiceReadDtoSchema = z.object({
  id: z.string().uuid(),
  tenantId: z.string().uuid(),
  kind: documentKindSchema,
  status: documentStatusSchema,
  buyerKind: buyerKindSchema,
  ksefSubmissionRequirement: ksefSubmissionRequirementSchema,
  ksefSubmissionState: ksefSubmissionStateSchema,
  seller: partyReadDtoSchema,
  buyer: partyReadDtoSchema,
  issueDate: z.string(),
  saleDate: z.string().nullable(),
  dueDate: z.string().nullable(),
  approvedAt: z.string().nullable(),
  submittedToKsefAt: z.string().nullable(),
  acceptedByKsefAt: z.string().nullable(),
  currency: z.string(),
  totalNet: moneyReadDtoSchema,
  totalVat: moneyReadDtoSchema,
  totalGross: moneyReadDtoSchema,
  documentNumber: z.string().nullable(),
  externalReference: z.string().nullable(),
  paymentMethod: z.string().nullable(),
  publicNotes: z.string().nullable(),
  internalNotes: z.string().nullable().optional(),
  ksefDocumentNumber: z.string().nullable(),
  ksefReferenceNumber: z.string().nullable(),
  ksefRejectionReason: z.string().nullable(),
  correctionReference: correctionReferenceReadDtoSchema.nullable(),
  lines: z.array(invoiceLineReadDtoSchema),
  advanceDocumentIds: z.array(z.string()),
  settledAdvanceAllocations: z.array(advanceAllocationReadDtoSchema),
  duplicateIssuances: z.array(duplicateIssuanceReadDtoSchema),
})

export const duplicatePrintInfoSchema = z.object({
  issuedAt: z.string(),
  issuedBy: z.string().nullable(),
  originalInvoiceId: z.string().uuid(),
  originalDocumentNumber: z.string(),
})

export const printLabelsSchema = z.object({
  invoiceTitle: z.string(),
  sellerLabel: z.string(),
  buyerLabel: z.string(),
  issueDateLabel: z.string(),
  saleDateLabel: z.string(),
  dueDateLabel: z.string(),
  documentNumberLabel: z.string(),
  currencyLabel: z.string(),
  totalNetLabel: z.string(),
  totalVatLabel: z.string(),
  totalGrossLabel: z.string(),
  lineDescriptionLabel: z.string(),
  lineQuantityLabel: z.string(),
  lineUnitPriceLabel: z.string(),
  lineNetAmountLabel: z.string(),
  lineVatRateLabel: z.string(),
  lineVatAmountLabel: z.string(),
  lineGrossAmountLabel: z.string(),
  duplicateLabel: z.string(),
})

export const invoicePrintModelSchema = z.object({
  invoiceData: invoiceReadDtoSchema,
  variant: printVariantSchema,
  labels: printLabelsSchema,
  duplicateInfo: duplicatePrintInfoSchema.nullable(),
})

export const validationEnvelopeMessageSchema = z.object({
  code: z.string().regex(/^INV-VAL-\d{3}$/),
  severity: validationSeveritySchema,
  field: z.string().nullable(),
  messagePl: z.string(),
  messageTechnical: z.string(),
})

export const validationEnvelopeSchema = z.object({
  stage: validationStageSchema,
  messages: z.array(validationEnvelopeMessageSchema),
})

export type DocumentKind = z.infer<typeof documentKindSchema>
export type DocumentStatus = z.infer<typeof documentStatusSchema>
export type BuyerKind = z.infer<typeof buyerKindSchema>
export type PricingMode = z.infer<typeof pricingModeSchema>
export type KsefSubmissionRequirement = z.infer<typeof ksefSubmissionRequirementSchema>
export type KsefSubmissionState = z.infer<typeof ksefSubmissionStateSchema>
export type CorrectionReasonKind = z.infer<typeof correctionReasonKindSchema>
export type CreateInvoiceRequest = z.infer<typeof createInvoiceRequestSchema>
export type UpdateInvoiceDraftRequest = z.infer<typeof updateInvoiceDraftRequestSchema>
export type CreateCorrectionFromOriginalRequest = z.infer<typeof createCorrectionFromOriginalRequestSchema>
export type CreateFinalInvoiceFromAdvancesRequest = z.infer<typeof createFinalInvoiceFromAdvancesRequestSchema>
export type InvoiceReadDto = z.infer<typeof invoiceReadDtoSchema>
export type DuplicatePrintInfo = z.infer<typeof duplicatePrintInfoSchema>
export type InvoicePrintModel = z.infer<typeof invoicePrintModelSchema>
export type ValidationEnvelope = z.infer<typeof validationEnvelopeSchema>

function isValidNip(value: string): boolean {
  const digits = value.split('').map((digit) => Number(digit))
  const weights = [6, 5, 7, 2, 3, 4, 5, 6, 7]
  const checksum = weights.reduce((sum, weight, index) => sum + weight * digits[index], 0) % 11

  return checksum !== 10 && checksum === digits[9]
}

function isValidIban(value: string): boolean {
  const normalized = `${value.slice(4)}${value.slice(0, 4)}`
    .split('')
    .map((character) => (/[A-Z]/.test(character) ? String(character.charCodeAt(0) - 55) : character))
    .join('')

  let remainder = 0
  for (const character of normalized) {
    remainder = (remainder * 10 + Number(character)) % 97
  }

  return remainder === 1
}
