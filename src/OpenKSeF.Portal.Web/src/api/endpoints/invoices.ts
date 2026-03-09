import { apiClient } from '../client'
import type { InvoiceResponse, PagedResult, TransferDetailsResponse } from '../types'

export interface ListInvoicesParams extends Record<string, string | number | boolean | null | undefined> {
  page?: number
  pageSize?: number
  filterByVendor?: string
  dateFrom?: string
  dateTo?: string
}

export function listInvoices(tenantId: string, params?: ListInvoicesParams): Promise<PagedResult<InvoiceResponse>> {
  return apiClient.get<PagedResult<InvoiceResponse>>(`/tenants/${encodeURIComponent(tenantId)}/invoices`, {
    query: params,
  })
}

export function getInvoice(tenantId: string, invoiceId: string): Promise<InvoiceResponse> {
  return apiClient.get<InvoiceResponse>(
    `/tenants/${encodeURIComponent(tenantId)}/invoices/${encodeURIComponent(invoiceId)}`,
  )
}

export function getInvoiceByKSeFNumber(tenantId: string, ksefNumber: string): Promise<InvoiceResponse> {
  return apiClient.get<InvoiceResponse>(
    `/tenants/${encodeURIComponent(tenantId)}/invoices/by-number/${encodeURIComponent(ksefNumber)}`,
  )
}

export function getTransferDetails(tenantId: string, invoiceId: string): Promise<TransferDetailsResponse> {
  return apiClient.get<TransferDetailsResponse>(
    `/tenants/${encodeURIComponent(tenantId)}/invoices/${encodeURIComponent(invoiceId)}/transfer`,
  )
}

export function setInvoicePaid(tenantId: string, invoiceId: string, isPaid: boolean): Promise<InvoiceResponse> {
  return apiClient.patch<InvoiceResponse>(
    `/tenants/${encodeURIComponent(tenantId)}/invoices/${encodeURIComponent(invoiceId)}/paid`,
    { isPaid },
  )
}
