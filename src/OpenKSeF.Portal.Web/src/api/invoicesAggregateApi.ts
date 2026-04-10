import { z } from 'zod'

import { apiClient } from './client'
import { ApiError } from './errors'
import {
  createCorrectionFromOriginalRequestSchema,
  createFinalInvoiceFromAdvancesRequestSchema,
  createInvoiceRequestSchema,
  duplicatePrintInfoSchema,
  invoicePrintModelSchema,
  invoiceReadDtoSchema,
  printVariantSchema,
  updateInvoiceDraftRequestSchema,
  validationEnvelopeSchema,
  type BuyerKind,
  type CreateCorrectionFromOriginalRequest,
  type CreateFinalInvoiceFromAdvancesRequest,
  type CreateInvoiceRequest,
  type DocumentKind,
  type DocumentStatus,
  type DuplicatePrintInfo,
  type InvoicePrintModel,
  type InvoiceReadDto,
  type ValidationEnvelope,
} from './schemas/invoice'

const pagedInvoiceReadDtoSchema = z.object({
  items: z.array(invoiceReadDtoSchema),
  page: z.number().int(),
  pageSize: z.number().int(),
  totalCount: z.number().int(),
  totalPages: z.number().int(),
})

const recordKsefAcceptanceRequestSchema = z.object({
  ksefDocumentNumber: z.string().trim().min(1),
  ksefReferenceNumber: z.string().trim().min(1),
  acceptedAt: z.string().trim().min(1),
})

const recordKsefRejectionRequestSchema = z.object({
  rejectionReason: z.string().trim().min(1),
  rejectedAt: z.string().trim().min(1),
})

export interface ListAggregateInvoicesParams {
  status?: DocumentStatus[]
  kind?: DocumentKind[]
  buyerKind?: BuyerKind
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

export interface RecordKsefAcceptanceRequest {
  ksefDocumentNumber: string
  ksefReferenceNumber: string
  acceptedAt: string
}

export interface RecordKsefRejectionRequest {
  rejectionReason: string
  rejectedAt: string
}

export type AggregateInvoicesPage = z.infer<typeof pagedInvoiceReadDtoSchema>
export type PrintVariant = z.infer<typeof printVariantSchema>

export class InvoiceValidationError extends ApiError {
  readonly stage: ValidationEnvelope['stage']
  readonly messages: ValidationEnvelope['messages']

  constructor(status: number, envelope: ValidationEnvelope, originalError?: unknown) {
    super(envelope.messages[0]?.messagePl ?? 'Invoice validation failed.', status, originalError)
    this.name = 'InvoiceValidationError'
    this.stage = envelope.stage
    this.messages = envelope.messages
  }
}

export function listAggregateInvoices(
  tenantId: string,
  params?: ListAggregateInvoicesParams,
): Promise<AggregateInvoicesPage> {
  const path = withSearch(buildAggregateBasePath(tenantId), (searchParams) => {
    for (const status of params?.status ?? []) {
      searchParams.append('status', status)
    }

    for (const kind of params?.kind ?? []) {
      searchParams.append('kind', kind)
    }

    if (params?.buyerKind) {
      searchParams.set('buyerKind', params.buyerKind)
    }

    if (params?.from) {
      searchParams.set('from', params.from)
    }

    if (params?.to) {
      searchParams.set('to', params.to)
    }

    if (params?.page !== undefined) {
      searchParams.set('page', String(params.page))
    }

    if (params?.pageSize !== undefined) {
      searchParams.set('pageSize', String(params.pageSize))
    }
  })

  return runRequest(() => apiClient.get(path), pagedInvoiceReadDtoSchema)
}

export function getAggregateInvoice(tenantId: string, invoiceId: string): Promise<InvoiceReadDto> {
  return runRequest(() => apiClient.get(buildAggregateItemPath(tenantId, invoiceId)), invoiceReadDtoSchema)
}

export function createInvoiceDraft(tenantId: string, request: CreateInvoiceRequest): Promise<InvoiceReadDto> {
  return runRequest(
    () => apiClient.post(buildAggregateBasePath(tenantId), createInvoiceRequestSchema.parse(request)),
    invoiceReadDtoSchema,
  )
}

export function updateInvoiceDraft(
  tenantId: string,
  invoiceId: string,
  request: z.input<typeof updateInvoiceDraftRequestSchema>,
): Promise<InvoiceReadDto> {
  return runRequest(
    () =>
      apiClient.patch(
        `${buildAggregateItemPath(tenantId, invoiceId)}/draft`,
        updateInvoiceDraftRequestSchema.parse(request),
      ),
    invoiceReadDtoSchema,
  )
}

export function approveInvoice(tenantId: string, invoiceId: string): Promise<InvoiceReadDto> {
  return runRequest(() => apiClient.post(`${buildAggregateItemPath(tenantId, invoiceId)}/approve`, {}), invoiceReadDtoSchema)
}

export function reopenInvoice(tenantId: string, invoiceId: string): Promise<InvoiceReadDto> {
  return runRequest(() => apiClient.post(`${buildAggregateItemPath(tenantId, invoiceId)}/reopen`, {}), invoiceReadDtoSchema)
}

export function submitInvoiceToKsef(tenantId: string, invoiceId: string): Promise<InvoiceReadDto> {
  return runRequest(() => apiClient.post(`${buildAggregateItemPath(tenantId, invoiceId)}/submit-to-ksef`), invoiceReadDtoSchema)
}

export function recordKsefAcceptance(
  tenantId: string,
  invoiceId: string,
  request: RecordKsefAcceptanceRequest,
): Promise<InvoiceReadDto> {
  return runRequest(
    () =>
      apiClient.post(
        `${buildAggregateItemPath(tenantId, invoiceId)}/ksef-acceptance`,
        recordKsefAcceptanceRequestSchema.parse(request),
      ),
    invoiceReadDtoSchema,
  )
}

export function recordKsefRejection(
  tenantId: string,
  invoiceId: string,
  request: RecordKsefRejectionRequest,
): Promise<InvoiceReadDto> {
  return runRequest(
    () =>
      apiClient.post(
        `${buildAggregateItemPath(tenantId, invoiceId)}/ksef-rejection`,
        recordKsefRejectionRequestSchema.parse(request),
      ),
    invoiceReadDtoSchema,
  )
}

export function createCorrectionFromOriginal(
  tenantId: string,
  invoiceId: string,
  request: CreateCorrectionFromOriginalRequest,
): Promise<InvoiceReadDto> {
  return runRequest(
    () =>
      apiClient.post(
        `${buildAggregateItemPath(tenantId, invoiceId)}/corrections`,
        createCorrectionFromOriginalRequestSchema.parse(request),
      ),
    invoiceReadDtoSchema,
  )
}

export function createFinalInvoiceFromAdvances(
  tenantId: string,
  request: CreateFinalInvoiceFromAdvancesRequest,
): Promise<InvoiceReadDto> {
  return runRequest(
    () =>
      apiClient.post(
        `${buildAggregateBasePath(tenantId)}/final-from-advances`,
        createFinalInvoiceFromAdvancesRequestSchema.parse(request),
      ),
    invoiceReadDtoSchema,
  )
}

export function getInvoicePrint(
  tenantId: string,
  invoiceId: string,
  variant: PrintVariant = 'Standard',
): Promise<InvoicePrintModel> {
  const path = withSearch(`${buildAggregateItemPath(tenantId, invoiceId)}/print`, (searchParams) => {
    searchParams.set('variant', printVariantSchema.parse(variant))
  })

  return runRequest(() => apiClient.get(path), invoicePrintModelSchema)
}

export function listInvoiceDuplicates(tenantId: string, invoiceId: string): Promise<DuplicatePrintInfo[]> {
  return runRequest(() => apiClient.get(`${buildAggregateItemPath(tenantId, invoiceId)}/duplicates`), z.array(duplicatePrintInfoSchema))
}

async function runRequest<T>(request: () => Promise<unknown>, schema: z.ZodType<T>): Promise<T> {
  try {
    return schema.parse(await request())
  } catch (error) {
    return mapValidationEnvelopeError(error)
  }
}

async function mapValidationEnvelopeError(error: unknown): Promise<never> {
  if (error instanceof ApiError && error.originalError instanceof Response && isValidationEnvelopeStatus(error.status)) {
    const envelope = await tryParseValidationEnvelope(error.originalError)
    if (envelope) {
      throw new InvoiceValidationError(error.status, envelope, error.originalError)
    }
  }

  throw error
}

async function tryParseValidationEnvelope(response: Response): Promise<ValidationEnvelope | null> {
  try {
    return validationEnvelopeSchema.parse(await response.clone().json())
  } catch {
    return null
  }
}

function isValidationEnvelopeStatus(status: number): boolean {
  return status === 409 || status === 422
}

function buildAggregateBasePath(tenantId: string): string {
  return `/tenants/${encodeURIComponent(tenantId)}/invoices/aggregate`
}

function buildAggregateItemPath(tenantId: string, invoiceId: string): string {
  return `${buildAggregateBasePath(tenantId)}/${encodeURIComponent(invoiceId)}`
}

function withSearch(path: string, populate: (searchParams: URLSearchParams) => void): string {
  const searchParams = new URLSearchParams()
  populate(searchParams)
  const queryString = searchParams.toString()

  return queryString.length > 0 ? `${path}?${queryString}` : path
}
