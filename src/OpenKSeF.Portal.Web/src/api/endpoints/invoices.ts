import { apiClient, type FileResponse } from '../client'
import type { InvoiceResponse, PagedResult, TransferDetailsResponse } from '../types'

export interface ListInvoicesParams extends Record<string, string | number | boolean | null | undefined> {
  page?: number
  pageSize?: number
  filterByVendor?: string
  dateFrom?: string
  dateTo?: string
  isPaid?: boolean
}

export function listInvoices(tenantId: string, params?: ListInvoicesParams): Promise<PagedResult<InvoiceResponse>> {
  return apiClient.get<PagedResult<InvoiceResponse>>(`/tenants/${encodeURIComponent(tenantId)}/invoices`, {
    query: params,
  })
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

export function getInvoiceXml(tenantId: string, invoiceId: string): Promise<FileResponse> {
  return apiClient.getFile(`/tenants/${encodeURIComponent(tenantId)}/invoices/${encodeURIComponent(invoiceId)}/xml`)
}

export interface ExportInvoicesParams extends Record<string, string | boolean | undefined> {
  from?: string
  to?: string
  sales: boolean
  purchases: boolean
}

export function exportInvoices(tenantId: string, params: ExportInvoicesParams): Promise<FileResponse> {
  return apiClient.getFile(`/tenants/${encodeURIComponent(tenantId)}/invoices/export`, { query: params })
}
