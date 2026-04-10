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

export const createInvoiceRequestSchema = z.object({
  kind: documentKindSchema,
  sellerName: z.string().trim().min(1),
  sellerNip: nipSchema,
  buyerName: z.string().trim().min(1),
  buyerKind: buyerKindSchema,
  buyerNip: nipSchema.nullish(),
  currency: currencyCodeSchema,
  issueDate: isoDateSchema,
  ksefSubmissionRequirement: ksefSubmissionRequirementSchema,
  documentNumber: z.string().trim().min(1).nullish(),
  externalReference: z.string().trim().min(1).nullish(),
})

export const updateInvoiceDraftRequestSchema = z.object({
  issueDate: isoDateSchema,
  saleDate: isoDateSchema.nullish(),
  dueDate: isoDateSchema.nullish(),
  documentNumber: z.string().trim().min(1).nullish(),
  paymentMethod: z.string().trim().min(1).nullish(),
  publicNotes: z.string().trim().min(1).nullish(),
  internalNotes: z.string().trim().min(1).nullish(),
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
