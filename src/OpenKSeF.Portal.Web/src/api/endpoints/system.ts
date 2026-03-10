import { ApiClient } from '../client'

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
