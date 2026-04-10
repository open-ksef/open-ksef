import { apiClient } from '../client'
import type { SyncedInvoiceResponse, PagedResult, TransferDetailsResponse } from '../types'

export interface ListInvoicesParams extends Record<string, string | number | boolean | null | undefined> {
  page?: number
  pageSize?: number
  filterByVendor?: string
  dateFrom?: string
  dateTo?: string
}

export function listInvoices(tenantId: string, params?: ListInvoicesParams): Promise<PagedResult<SyncedInvoiceResponse>> {
  return apiClient.get<PagedResult<SyncedInvoiceResponse>>(`/tenants/${encodeURIComponent(tenantId)}/invoices`, {
    query: params,
  })
}

export function getInvoice(tenantId: string, invoiceId: string): Promise<SyncedInvoiceResponse> {
  return apiClient.get<SyncedInvoiceResponse>(
    `/tenants/${encodeURIComponent(tenantId)}/invoices/${encodeURIComponent(invoiceId)}`,
  )
}

export function getInvoiceByKSeFNumber(tenantId: string, ksefNumber: string): Promise<SyncedInvoiceResponse> {
  return apiClient.get<SyncedInvoiceResponse>(
    `/tenants/${encodeURIComponent(tenantId)}/invoices/by-number/${encodeURIComponent(ksefNumber)}`,
  )
}

export function getTransferDetails(tenantId: string, invoiceId: string): Promise<TransferDetailsResponse> {
  return apiClient.get<TransferDetailsResponse>(
    `/tenants/${encodeURIComponent(tenantId)}/invoices/${encodeURIComponent(invoiceId)}/transfer`,
  )
}

export function setInvoicePaid(tenantId: string, invoiceId: string, isPaid: boolean): Promise<SyncedInvoiceResponse> {
  return apiClient.patch<SyncedInvoiceResponse>(
    `/tenants/${encodeURIComponent(tenantId)}/invoices/${encodeURIComponent(invoiceId)}/paid`,
    { isPaid },
  )
}
