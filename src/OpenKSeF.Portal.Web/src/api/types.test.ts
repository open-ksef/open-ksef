import { describe, expect, it } from 'vitest'

import type {
  CreateTenantRequest,
  CredentialStatusResponse,
  DeviceTokenResponse,
  InvoiceResponse,
  PagedResult,
  TenantDashboardSummaryResponse,
  TenantCredentialStatusResponse,
  TenantResponse,
  UpdateTenantRequest,
} from './types'

describe('API type contracts', () => {
  it('supports tenant request/response contracts', () => {
    const tenant = {
      id: 'tenant-id',
      nip: '1234567890',
      displayName: 'Main tenant',
      notificationEmail: 'ops@example.com',
      createdAt: '2026-02-27T00:00:00Z',
    } satisfies TenantResponse

    const createPayload = {
      nip: '1234567890',
      displayName: 'Main tenant',
      notificationEmail: 'ops@example.com',
    } satisfies CreateTenantRequest

    const updatePayload = {
      displayName: 'Updated name',
      notificationEmail: null,
    } satisfies UpdateTenantRequest

    expect(tenant.id).toBe('tenant-id')
    expect(createPayload.nip).toBe('1234567890')
    expect(updatePayload.displayName).toBe('Updated name')
  })

  it('supports paged invoice and dashboard contracts', () => {
    const invoice = {
      id: 'invoice-id',
      ksefInvoiceNumber: 'KSEF-1',
      ksefReferenceNumber: 'REF-1',
      invoiceNumber: 'FV/2026/001',
      vendorName: 'Vendor',
      vendorNip: '1234567890',
      buyerName: 'Buyer',
      buyerNip: '0987654321',
      amountNet: 100.0,
      amountVat: 20.5,
      amountGross: 120.5,
      currency: 'PLN',
      issueDate: '2026-02-27T00:00:00Z',
      acquisitionDate: '2026-02-27T00:05:00Z',
      invoiceType: 'VAT',
      firstSeenAt: '2026-02-27T01:00:00Z',
      isPaid: false,
      paidAt: null,
    } satisfies InvoiceResponse

    const paged: PagedResult<InvoiceResponse> = {
      items: [invoice],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    }

    const dashboardItem = {
      tenantId: 'tenant-id',
      nip: '1234567890',
      displayName: 'Main tenant',
      lastSyncedAt: '2026-02-27T00:00:00Z',
      lastSuccessfulSync: '2026-02-27T00:00:00Z',
      totalInvoices: 42,
      invoicesLast7Days: 7,
      invoicesLast30Days: 30,
      syncStatus: 'Success',
    } satisfies TenantDashboardSummaryResponse

    expect(paged.items).toHaveLength(1)
    expect(dashboardItem.totalInvoices).toBe(42)
  })

  it('supports credential and device response contracts', () => {
    const status = {
      exists: true,
      credentialType: 'Token' as const,
      updatedAt: '2026-02-27T00:00:00Z',
    } satisfies CredentialStatusResponse

    const tenantStatus = {
      tenantId: 'tenant-id',
      tenantDisplayName: 'Tenant',
      hasCredential: true,
      credentialType: 'Token' as const,
      lastUpdatedAt: null,
    } satisfies TenantCredentialStatusResponse

    const device = {
      id: 'device-id',
      token: 'fcm-token',
      platform: 0,
      tenantId: null,
      createdAt: '2026-02-27T00:00:00Z',
      updatedAt: '2026-02-27T00:00:00Z',
    } satisfies DeviceTokenResponse

    expect(status.exists).toBe(true)
    expect(tenantStatus.lastUpdatedAt).toBeNull()
    expect(device.platform).toBe(0)
  })
})
