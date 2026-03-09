import { describe, expect, it } from 'vitest'

import * as api from './index'

describe('api index exports', () => {
  it('re-exports shared api utilities', () => {
    expect(api.ApiClient).toBeDefined()
    expect(api.apiClient).toBeDefined()
    expect(api.ApiError).toBeDefined()
    expect(api.normalizeError).toBeDefined()
  })

  it('exports endpoint namespaces', () => {
    expect(api.tenants.listTenants).toBeTypeOf('function')
    expect(api.invoices.listInvoices).toBeTypeOf('function')
    expect(api.credentials.listCredentials).toBeTypeOf('function')
    expect(api.devices.listDevices).toBeTypeOf('function')
    expect(api.dashboard.getDashboardOverview).toBeTypeOf('function')
  })
})
