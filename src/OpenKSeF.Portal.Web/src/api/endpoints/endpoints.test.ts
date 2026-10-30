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
    await unregisterDevice('device-id-guid')

    expect(apiClientMock.get).toHaveBeenCalledWith('/devices')
    expect(apiClientMock.post).toHaveBeenCalledWith('/devices/register', {
      token: 'device-token',
      platform: 0,
      tenantId: 'tenant-1',
    })
    expect(apiClientMock.delete).toHaveBeenCalledWith('/devices/device-id-guid')
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

describe('issuing endpoints', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls issued-invoices, seller-profile and nip-lookup routes from the contract', async () => {
    const issued = await import('./issuedInvoices')
    const { getSellerProfile, updateSellerProfile } = await import('./sellerProfile')
    const { lookupNip } = await import('./nipLookup')
    const draft = {
      buyer: null,
      issueDate: '2026-11-03',
      saleDate: null,
      dueDate: null,
      paymentMethod: null,
      splitPayment: false,
      exemptionBasis: null,
      lines: [],
    }

    await issued.listIssuedInvoices('t-1', { page: 1, pageSize: 20, status: 'Draft' })
    await issued.getIssuedInvoice('t-1', 'i-1')
    await issued.createIssuedInvoice('t-1', draft)
    await issued.updateIssuedInvoice('t-1', 'i-1', draft)
    await issued.deleteIssuedInvoice('t-1', 'i-1')
    await issued.issueIssuedInvoice('t-1', 'i-1')
    await issued.copyIssuedInvoice('t-1', 'i-1')
    await issued.getIssuedInvoiceXml('t-1', 'i-1')
    await issued.getIssuedInvoiceUpo('t-1', 'i-1')
    await getSellerProfile('t-1')
    await updateSellerProfile('t-1', {
      legalName: 'A',
      addressLine1: null,
      addressLine2: null,
      bankAccount: null,
      cashAccounting: false,
      numberingPattern: 'FV/{nr}/{RRRR}',
    })
    await lookupNip('5260250274')

    expect(apiClientMock.get.mock.calls).toEqual([
      ['/tenants/t-1/issued-invoices', { query: { page: 1, pageSize: 20, status: 'Draft' } }],
      ['/tenants/t-1/issued-invoices/i-1'],
      ['/tenants/t-1/issued-invoices/i-1/xml'],
      ['/tenants/t-1/issued-invoices/i-1/upo'],
      ['/tenants/t-1/seller-profile'],
      ['/nip-lookup/5260250274'],
    ])
    expect(apiClientMock.post.mock.calls).toEqual([
      ['/tenants/t-1/issued-invoices', draft],
      ['/tenants/t-1/issued-invoices/i-1/issue'],
      ['/tenants/t-1/issued-invoices/i-1/copy'],
    ])
    expect(apiClientMock.put.mock.calls[0][0]).toBe('/tenants/t-1/issued-invoices/i-1')
    expect(apiClientMock.put.mock.calls[1][0]).toBe('/tenants/t-1/seller-profile')
    expect(apiClientMock.delete).toHaveBeenCalledWith('/tenants/t-1/issued-invoices/i-1')
  })
})
