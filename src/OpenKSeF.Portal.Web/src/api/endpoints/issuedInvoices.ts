import { apiClient } from '../client'
import type {
  IssuedInvoiceDraftRequest,
  IssuedInvoiceListItemResponse,
  IssuedInvoiceResponse,
  IssuedInvoiceStatus,
  PagedResult,
} from '../types'

export interface ListIssuedInvoicesParams extends Record<string, string | number | boolean | null | undefined> {
  page?: number
  pageSize?: number
  status?: IssuedInvoiceStatus
}

const base = (tenantId: string): string => `/tenants/${encodeURIComponent(tenantId)}/issued-invoices`
const item = (tenantId: string, id: string): string => `${base(tenantId)}/${encodeURIComponent(id)}`

export function listIssuedInvoices(
  tenantId: string,
  params?: ListIssuedInvoicesParams,
): Promise<PagedResult<IssuedInvoiceListItemResponse>> {
  return apiClient.get<PagedResult<IssuedInvoiceListItemResponse>>(base(tenantId), { query: params })
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
): Promise<IssuedInvoiceResponse> {
  return apiClient.put<IssuedInvoiceResponse>(item(tenantId, id), request)
}

export function deleteIssuedInvoice(tenantId: string, id: string): Promise<void> {
  return apiClient.delete<void>(item(tenantId, id))
}

export function issueIssuedInvoice(tenantId: string, id: string): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(`${item(tenantId, id)}/issue`)
}

export function copyIssuedInvoice(tenantId: string, id: string): Promise<IssuedInvoiceResponse> {
  return apiClient.post<IssuedInvoiceResponse>(`${item(tenantId, id)}/copy`)
}

export function getIssuedInvoiceXml(tenantId: string, id: string): Promise<string> {
  return apiClient.get<string>(`${item(tenantId, id)}/xml`)
}

export function getIssuedInvoiceUpo(tenantId: string, id: string): Promise<string> {
  return apiClient.get<string>(`${item(tenantId, id)}/upo`)
}
