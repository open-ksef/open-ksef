import { beforeEach, describe, expect, it, vi } from 'vitest'

const apiClientMock = {
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}

vi.mock('../client', () => ({
  apiClient: apiClientMock,
}))

describe('tenants endpoints', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls list/get/create/update/delete tenant endpoints', async () => {
    const { listTenants, getTenant, createTenant, updateTenant, deleteTenant } = await import('./tenants')

    await listTenants()
    await getTenant('tenant-1')
    await createTenant({ nip: '1234567890', displayName: 'Main' })
    await updateTenant('tenant-1', { displayName: 'Updated' })
    await deleteTenant('tenant-1')

    expect(apiClientMock.get).toHaveBeenNthCalledWith(1, '/tenants')
    expect(apiClientMock.get).toHaveBeenNthCalledWith(2, '/tenants/tenant-1')
    expect(apiClientMock.post).toHaveBeenCalledWith('/tenants', { nip: '1234567890', displayName: 'Main' })
    expect(apiClientMock.put).toHaveBeenCalledWith('/tenants/tenant-1', { displayName: 'Updated' })
    expect(apiClientMock.delete).toHaveBeenCalledWith('/tenants/tenant-1')
  })
})

describe('invoices endpoints', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls invoices list/get/by-number endpoints with query support', async () => {
    const { listInvoices, getInvoice, getInvoiceByKSeFNumber } = await import('./invoices')

    await listInvoices('tenant-1', {
      page: 2,
      pageSize: 50,
      filterByVendor: 'Vendor',
      dateFrom: '2026-01-01',
      dateTo: '2026-01-31',
    })

    await getInvoice('tenant-1', 'invoice-1')
    await getInvoiceByKSeFNumber('tenant-1', 'KSEF-123')

    expect(apiClientMock.get).toHaveBeenNthCalledWith(1, '/tenants/tenant-1/invoices', {
      query: {
        page: 2,
        pageSize: 50,
        filterByVendor: 'Vendor',
        dateFrom: '2026-01-01',
        dateTo: '2026-01-31',
      },
    })

    expect(apiClientMock.get).toHaveBeenNthCalledWith(2, '/tenants/tenant-1/invoices/invoice-1')
    expect(apiClientMock.get).toHaveBeenNthCalledWith(3, '/tenants/tenant-1/invoices/by-number/KSEF-123')
  })
})

describe('credentials endpoints', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls credential summary/status/add/delete endpoints', async () => {
    const { listCredentials, getCredentialStatus, addOrUpdateCredential, deleteCredential } = await import('./credentials')

    await listCredentials()
    await getCredentialStatus('tenant-1')
    await addOrUpdateCredential('tenant-1', 'secret-token')
    await deleteCredential('tenant-1')

    expect(apiClientMock.get).toHaveBeenNthCalledWith(1, '/credentials')
    expect(apiClientMock.get).toHaveBeenNthCalledWith(2, '/tenants/tenant-1/credentials/status')
    expect(apiClientMock.post).toHaveBeenCalledWith('/tenants/tenant-1/credentials', { type: 'Token', token: 'secret-token' })
    expect(apiClientMock.delete).toHaveBeenCalledWith('/tenants/tenant-1/credentials')
  })
})

describe('devices endpoints', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls list/register/unregister device endpoints', async () => {
    const { listDevices, registerDevice, unregisterDevice } = await import('./devices')

    await listDevices()
    await registerDevice({
      token: 'device-token',
      platform: 0,
      tenantId: 'tenant-1',
    })
    await unregisterDevice('device-token')

    expect(apiClientMock.get).toHaveBeenCalledWith('/devices')
    expect(apiClientMock.post).toHaveBeenCalledWith('/devices/register', {
      token: 'device-token',
      platform: 0,
      tenantId: 'tenant-1',
    })
    expect(apiClientMock.delete).toHaveBeenCalledWith('/devices/device-token')
  })
})

describe('dashboard endpoints', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls dashboard overview endpoint', async () => {
    const { getDashboardOverview } = await import('./dashboard')

    await getDashboardOverview()

    expect(apiClientMock.get).toHaveBeenCalledWith('/dashboard')
  })
})
