import { describe, expect, it } from 'vitest'

import type { TenantCredentialStatusResponse, TenantDashboardSummaryResponse } from '@/api/types'
import { companyState } from './useCompanyState'

const NOW = Date.parse('2026-11-03T12:00:00Z')

const summary = (overrides: Partial<TenantDashboardSummaryResponse> = {}): TenantDashboardSummaryResponse => ({
  tenantId: 't',
  nip: '1111111111',
  displayName: 'A',
  lastSyncedAt: null,
  lastSuccessfulSync: null,
  totalInvoices: 0,
  invoicesLast7Days: 0,
  invoicesLast30Days: 0,
  syncStatus: 'Error',
  ...overrides,
})

const credential = (overrides: Partial<TenantCredentialStatusResponse> = {}): TenantCredentialStatusResponse => ({
  tenantId: 't',
  tenantDisplayName: 'A',
  hasCredential: true,
  credentialType: 'Token',
  lastUpdatedAt: '2026-11-03T11:00:00Z',
  ...overrides,
})

describe('companyState', () => {
  it.each([
    ['no token, no invoices', summary(), credential({ hasCredential: false, lastUpdatedAt: null }), 'no-token'],
    ['no token removed after a sync, no invoices', summary({ lastSuccessfulSync: '2026-11-01T00:00:00Z' }), credential({ hasCredential: false }), 'no-token'],
    ['no token with invoices keeps the normal page', summary({ totalInvoices: 69 }), credential({ hasCredential: false }), 'full'],
    ['token, never synced, under 24 h', summary(), credential(), 'first-fetch'],
    ['token, never synced, 1 ms under 24 h', summary(), credential({ lastUpdatedAt: new Date(NOW - 86_399_999).toISOString() }), 'first-fetch'],
    ['token, never synced, exactly 24 h', summary(), credential({ lastUpdatedAt: new Date(NOW - 86_400_000).toISOString() }), 'first-fetch-late'],
    ['token, never synced, 25 h', summary(), credential({ lastUpdatedAt: '2026-11-02T11:00:00Z' }), 'first-fetch-late'],
    ['never synced but invoices exist', summary({ totalInvoices: 1 }), credential(), 'full'],
    ['synced, no invoices', summary({ lastSuccessfulSync: '2026-11-03T10:00:00Z' }), credential(), 'zero'],
    ['synced, invoices', summary({ lastSuccessfulSync: '2026-11-03T10:00:00Z', totalInvoices: 3 }), credential(), 'full'],
  ])('%s', (_, row, cred, expected) => {
    expect(companyState(row, cred, NOW)).toBe(expected)
  })

  it('makes no claim while a query has no row', () => {
    expect(companyState(undefined, credential(), NOW)).toBeNull()
    expect(companyState(summary(), undefined, NOW)).toBeNull()
  })
})
