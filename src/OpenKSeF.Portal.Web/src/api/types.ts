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

export interface InvoiceLineResponse {
  lineNumber: number
  name: string | null
  unit: string | null
  quantity: number | null
  unitPriceNet: number | null
  unitPriceGross: number | null
  amountNet: number | null
  amountGross: number | null
  amountVat: number | null
  vatRate: string | null
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
  lines?: InvoiceLineResponse[] | null
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

export type KsefEnvironment = 'test' | 'demo' | 'production'

export type VatRateCode = '23' | '8' | '5' | '0 KR' | 'zw'

export type BuyerKind = 'Company' | 'Person'

export type PaymentMethod = 'Transfer' | 'Cash' | 'Card'

export type ExemptionBasisKind = 'Act' | 'Directive' | 'Other'

export type IssuedInvoiceStatus = 'Draft' | 'Sending' | 'Accepted' | 'Rejected'

export interface ValidationIssue {
  code: string
  field: string | null
  message: string
}

export interface ValidationEnvelope {
  errors: ValidationIssue[]
  warnings: ValidationIssue[]
}

export interface SellerProfileRequest {
  legalName: string | null
  addressLine1: string | null
  addressLine2: string | null
  bankAccount: string | null
  cashAccounting: boolean
  numberingPattern: string | null
  nextNumber?: number | null
  nextNumberPeriod?: string | null
}

export interface SellerProfileResponse {
  legalName: string | null
  addressLine1: string | null
  addressLine2: string | null
  bankAccount: string | null
  cashAccounting: boolean
  numberingPattern: string
  nextNumber: number
  nextNumberPeriod: string
  nip: string
  isComplete: boolean
  nextNumberPreview: string
  ksefEnvironment: KsefEnvironment
}

export interface NipLookupResponse {
  nip: string
  name: string
  addressLine1: string | null
  addressLine2: string | null
  vatStatus: string
  bankAccounts: string[]
}

export interface IssuedInvoiceBuyer {
  kind: BuyerKind
  nip: string | null
  name: string | null
  addressLine1: string | null
  addressLine2: string | null
}

export interface ExemptionBasis {
  kind: ExemptionBasisKind
  text: string | null
}

export interface IssuedInvoiceLineRequest {
  name: string | null
  unit: string | null
  quantity: number
  unitNetPrice: number
  vatRate: VatRateCode | null
}

export interface IssuedInvoiceDraftRequest {
  buyer: IssuedInvoiceBuyer | null
  issueDate: string | null
  saleDate: string | null
  dueDate: string | null
  paymentMethod: PaymentMethod | null
  splitPayment: boolean
  exemptionBasis: ExemptionBasis | null
  lines: IssuedInvoiceLineRequest[] | null
}

export interface IssuedInvoiceSeller {
  nip: string
  name: string | null
  addressLine1: string | null
  addressLine2: string | null
  bankAccount: string | null
  cashAccounting: boolean
}

export interface IssuedInvoiceLineResponse {
  lineNo: number
  name: string
  unit: string
  quantity: number
  unitNetPrice: number
  vatRate: VatRateCode
  netAmount: number
  vatAmount: number
}

export interface VatSummaryRow {
  vatRate: VatRateCode
  net: number
  vat: number
}

export interface IssuedInvoiceKsef {
  environment: KsefEnvironment
  ksefNumber: string | null
  acceptedAt: string | null
  statusCode: number | null
  statusMessage: string | null
  lastError: string | null
  verificationUrl: string | null
  hasUpo: boolean
}

export interface IssuedInvoiceResponse {
  id: string
  status: IssuedInvoiceStatus
  number: string | null
  seller: IssuedInvoiceSeller
  buyer: IssuedInvoiceBuyer
  issueDate: string
  saleDate: string
  dueDate: string
  paymentMethod: PaymentMethod
  splitPayment: boolean
  exemptionBasis: ExemptionBasis | null
  lines: IssuedInvoiceLineResponse[]
  vatSummary: VatSummaryRow[]
  totalNet: number
  totalVat: number
  totalGross: number
  currency: string
  ksef: IssuedInvoiceKsef
  warnings: ValidationIssue[]
  createdAt: string
  updatedAt: string
  issuedAt: string | null
}

export interface IssuedInvoiceListItemResponse {
  id: string
  status: IssuedInvoiceStatus
  number: string | null
  buyerName: string | null
  buyerNip: string | null
  issueDate: string
  totalGross: number
  currency: string
  ksefNumber: string | null
}
