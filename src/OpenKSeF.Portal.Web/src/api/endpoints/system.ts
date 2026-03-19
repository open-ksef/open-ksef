import { ApiClient, apiClient } from '../client'

const anonClient = new ApiClient(import.meta.env.VITE_API_BASE_URL ?? '/api')

export interface SetupStatusResponse {
  isInitialized: boolean
}

export interface SetupAuthenticateRequest {
  username: string
  password: string
}

export interface SetupAuthenticateResponse {
  setupToken: string
  expiresInSeconds: number
}

export interface SmtpConfig {
  host: string
  port: string
  from: string
  fromDisplayName?: string
  replyTo?: string
  starttls: boolean
  ssl: boolean
  auth: boolean
  user?: string
  password?: string
}

export interface SetupApplyRequest {
  externalBaseUrl: string
  kSeFBaseUrl: string
  adminEmail: string
  adminPassword: string
  adminFirstName?: string
  adminLastName?: string
  firstTenantNip?: string
  firstTenantDisplayName?: string
  registrationAllowed: boolean
  verifyEmail: boolean
  loginWithEmailAllowed: boolean
  resetPasswordAllowed: boolean
  passwordPolicy?: string
  smtp?: SmtpConfig
  googleClientId?: string
  googleClientSecret?: string
  pushRelayUrl?: string
  pushRelayApiKey?: string
  firebaseCredentialsJson?: string
  newKeycloakAdminPassword?: string
}

export interface SetupApplyResponse {
  success: boolean
  encryptionKey?: string
  apiClientSecret?: string
  error?: string
}

export function getSetupStatus(): Promise<SetupStatusResponse> {
  return anonClient.get<SetupStatusResponse>('/system/setup-status')
}

export function authenticateAdmin(
  request: SetupAuthenticateRequest,
): Promise<SetupAuthenticateResponse> {
  return anonClient.post<SetupAuthenticateResponse>('/system/setup/authenticate', request)
}

export function applySetup(
  setupToken: string,
  request: SetupApplyRequest,
): Promise<SetupApplyResponse> {
  return anonClient.post<SetupApplyResponse>('/system/setup/apply', request, {
    headers: { 'X-Setup-Token': setupToken },
  })
}

// --- Settings API (post-setup, requires KC admin creds) ---

export interface SettingsAuthRequest {
  kcAdminUsername: string
  kcAdminPassword: string
}

export interface SettingsResponse {
  externalBaseUrl?: string
  kSeFEnvironment?: string
  kSeFEnvironmentLocked: boolean
  kSeFEnvironmentLockReason?: string
  registrationAllowed: boolean
  verifyEmail: boolean
  loginWithEmailAllowed: boolean
  resetPasswordAllowed: boolean
  passwordPolicy?: string
  smtp?: SmtpConfig
  googleClientId?: string
  googleConfigured: boolean
  pushRelayUrl?: string
  pushRelayApiKey?: string
  pushRelayInstanceId?: string
  firebaseConfigured: boolean
}

export interface SettingsUpdateRequest {
  kcAdminUsername: string
  kcAdminPassword: string
  externalBaseUrl?: string
  kSeFEnvironment?: string
  registrationAllowed?: boolean
  verifyEmail?: boolean
  loginWithEmailAllowed?: boolean
  resetPasswordAllowed?: boolean
  passwordPolicy?: string
  smtp?: SmtpConfig
  clearSmtp?: boolean
  googleClientId?: string
  googleClientSecret?: string
  pushRelayUrl?: string
  pushRelayApiKey?: string
  reRegisterRelay?: boolean
  firebaseCredentialsJson?: string
  confirmCredentialWipe?: boolean
}

export interface SettingsUpdateResponse {
  success: boolean
  error?: string
}

export function getSettings(
  kcAdminUsername: string,
  kcAdminPassword: string,
): Promise<SettingsResponse> {
  return apiClient.post<SettingsResponse>('/system/settings', {
    kcAdminUsername,
    kcAdminPassword,
  })
}

export function updateSettings(
  request: SettingsUpdateRequest,
): Promise<SettingsUpdateResponse> {
  return apiClient.put<SettingsUpdateResponse>('/system/settings', request)
}
