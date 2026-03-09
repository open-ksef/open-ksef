export interface TenantResponse {
  id: string
  nip: string
  displayName: string | null
  notificationEmail: string | null
  createdAt: string
}

export interface CreateTenantRequest {
  nip: string
  displayName?: string | null
  notificationEmail?: string | null
}

export interface UpdateTenantRequest {
  displayName?: string | null
  notificationEmail?: string | null
}

export interface InvoiceResponse {
  id: string
  ksefInvoiceNumber: string
  ksefReferenceNumber: string
  invoiceNumber: string | null
  vendorName: string
  vendorNip: string
  buyerName: string | null
  buyerNip: string | null
  amountNet: number
  amountVat: number
  amountGross: number
  currency: string
  issueDate: string
  acquisitionDate: string | null
  invoiceType: string | null
  firstSeenAt: string
  isPaid: boolean
  paidAt: string | null
}

export interface TransferDetailsResponse {
  recipientName: string
  recipientAccount: string | null
  recipientNip: string
  amount: number
  currency: string
  title: string
  transferText: string
  qrCodeBase64: string
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export type CredentialType = 'Token' | 'Certificate'

export interface CredentialStatusResponse {
  exists: boolean
  credentialType: CredentialType | null
  updatedAt: string | null
}

export interface TenantCredentialStatusResponse {
  tenantId: string
  tenantDisplayName: string
  hasCredential: boolean
  credentialType: CredentialType | null
  lastUpdatedAt: string | null
}

export interface TenantManualSyncResponse {
  tenantId: string
  fetchedInvoices: number
  newInvoices: number
  syncedAtUtc: string
}

export interface DeviceTokenResponse {
  id: string
  token: string
  platform: number
  tenantId: string | null
  createdAt: string
  updatedAt: string
}

export interface OnboardingStatusResponse {
  isComplete: boolean
  hasTenant: boolean
  hasCredential: boolean
  firstTenantId: string | null
}

export interface TenantDashboardSummaryResponse {
  tenantId: string
  nip: string
  displayName: string | null
  lastSyncedAt: string | null
  lastSuccessfulSync: string | null
  totalInvoices: number
  invoicesLast7Days: number
  invoicesLast30Days: number
  syncStatus: 'Success' | 'Warning' | 'Error' | string
}
