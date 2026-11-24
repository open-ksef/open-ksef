import { apiClient } from '../client'
import type {
  AdvanceDraftRequest,
  BuyerKind,
  CorrectionDraftRequest,
  CorrectionMode,
  ExchangeRateProposalResponse,
  FinalDraftRequest,
  IssuedFeedItemResponse,
  IssuedInvoiceDraftRequest,
  IssuedInvoiceListItemResponse,
  IssuedInvoiceResponse,
  IssuedInvoiceStatus,
  IssuedInvoiceType,
  InvoiceTemplateResponse,
  LineSuggestionResponse,
  PagedResult,
  RecentBuyerResponse,
} from '../types'

export interface ListIssuedInvoicesParams extends Record<string, string | number | boolean | null | undefined> {
  page?: number
  pageSize?: number
  status?: IssuedInvoiceStatus
  month?: string
  q?: string
}

const base = (tenantId: string): string => `/tenants/${encodeURIComponent(tenantId)}/issued-invoices`
const item = (tenantId: string, id: string): string => `${base(tenantId)}/${encodeURIComponent(id)}`

export function listIssuedInvoices(
  tenantId: string,
  params?: ListIssuedInvoicesParams,
): Promise<PagedResult<IssuedInvoiceListItemResponse>> {
  return apiClient.get<PagedResult<IssuedInvoiceListItemResponse>>(base(tenantId), { query: params })
}

export function listIssuedFeed(
  tenantId: string,
  params?: ListIssuedInvoicesParams,
): Promise<PagedResult<IssuedFeedItemResponse> & { issuedFetchIncomplete: boolean }> {
  return apiClient.get<PagedResult<IssuedFeedItemResponse> & { issuedFetchIncomplete: boolean }>(`${base(tenantId)}/feed`, { query: params })
}

export function getExchangeRateProposal(
  tenantId: string,
  query: { currency: string; issueDate: string; saleDate: string },
): Promise<ExchangeRateProposalResponse> {
  return apiClient.get<ExchangeRateProposalResponse>(`${base(tenantId)}/exchange-rate`, { query })
}

export function getIssuedInvoice(tenantId: string, id: string): Promise<IssuedInvoiceResponse> {
  return apiClient.get<IssuedInvoiceResponse>(item(tenantId, id))
}

export function createIssuedInvoice(tenantId: string, request: IssuedInvoiceDraftRequest): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(base(tenantId), request)
}

export function updateIssuedInvoice(
  tenantId: string,
  id: string,
  request: IssuedInvoiceDraftRequest,
  version?: number,
): Promise<IssuedInvoiceResponse> {
  return apiClient.put<IssuedInvoiceResponse>(item(tenantId, id), request, { query: { version } })
}

export function deleteIssuedInvoice(tenantId: string, id: string, version?: number): Promise<void> {
  return apiClient.delete<void>(item(tenantId, id), { query: { version } })
}

export function issueIssuedInvoice(
  tenantId: string,
  id: string,
  version?: number,
  sellerVersion?: number,
  confirmPastIssueDate?: boolean,
  invoiceType?: IssuedInvoiceType,
): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(`${item(tenantId, id)}/issue`, undefined, {
    query: { version, sellerVersion, confirmPastIssueDate, invoiceType },
  })
}

export function createCorrection(tenantId: string, id: string, mode: CorrectionMode): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(`${item(tenantId, id)}/corrections`, { mode })
}

export function updateCorrection(tenantId: string, id: string, request: CorrectionDraftRequest, version?: number): Promise<IssuedInvoiceResponse> {
  return apiClient.put<IssuedInvoiceResponse>(`${item(tenantId, id)}/correction`, request, { query: { version } })
}

export function createAdvance(tenantId: string, fromAdvanceId?: string): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(fromAdvanceId ? `${item(tenantId, fromAdvanceId)}/advances` : `${base(tenantId)}/advances`, {})
}

export function updateAdvance(tenantId: string, id: string, request: AdvanceDraftRequest, version?: number): Promise<IssuedInvoiceResponse> {
  return apiClient.put<IssuedInvoiceResponse>(`${item(tenantId, id)}/advance`, request, { query: { version } })
}

export function createFinal(tenantId: string, advanceId: string): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(`${item(tenantId, advanceId)}/final`)
}

export function updateFinal(tenantId: string, id: string, request: FinalDraftRequest, version?: number): Promise<IssuedInvoiceResponse> {
  return apiClient.put<IssuedInvoiceResponse>(`${item(tenantId, id)}/final`, request, { query: { version } })
}

export function reissueInvoice(tenantId: string, id: string): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(`${item(tenantId, id)}/reissue`)
}

export function copyIssuedInvoice(tenantId: string, id: string): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(`${item(tenantId, id)}/copy`)
}

export function listRecentBuyers(tenantId: string, q?: string): Promise<RecentBuyerResponse[]> {
  return apiClient.get<RecentBuyerResponse[]>(`${base(tenantId)}/recent-buyers`, { query: { q: q || undefined } })
}

export function listLineSuggestions(tenantId: string, q: string, buyerKind: BuyerKind): Promise<LineSuggestionResponse[]> {
  return apiClient.get<LineSuggestionResponse[]>(`${base(tenantId)}/line-suggestions`, { query: { q, buyerKind } })
}

const template = (tenantId: string, id: string): string => `${base(tenantId)}/templates/${encodeURIComponent(id)}`

export function listTemplates(tenantId: string): Promise<InvoiceTemplateResponse[]> {
  return apiClient.get<InvoiceTemplateResponse[]>(`${base(tenantId)}/templates`)
}

export function saveAsTemplate(tenantId: string, id: string, name: string): Promise<InvoiceTemplateResponse> {
  return apiClient.post<InvoiceTemplateResponse>(`${item(tenantId, id)}/template`, { name })
}

export function renameTemplate(tenantId: string, id: string, name: string): Promise<InvoiceTemplateResponse> {
  return apiClient.put<InvoiceTemplateResponse>(template(tenantId, id), { name })
}

export function deleteTemplate(tenantId: string, id: string): Promise<void> {
  return apiClient.delete<void>(template(tenantId, id))
}

export function createDraftFromTemplate(tenantId: string, id: string): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(`${template(tenantId, id)}/draft`)
}

export function getIssuedInvoiceXml(tenantId: string, id: string): Promise<string> {
  return apiClient.get<string>(`${item(tenantId, id)}/xml`)
}

export function getIssuedInvoiceUpo(tenantId: string, id: string): Promise<string> {
  return apiClient.get<string>(`${item(tenantId, id)}/upo`)
}
