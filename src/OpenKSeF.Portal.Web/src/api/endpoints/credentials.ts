import { apiClient } from '../client'
import type { CredentialStatusResponse, TenantCredentialStatusResponse, TenantManualSyncResponse } from '../types'

interface MessageResponse {
  message: string
}

export function listCredentials(): Promise<TenantCredentialStatusResponse[]> {
  return apiClient.get<TenantCredentialStatusResponse[]>('/credentials')
}

export function getCredentialStatus(tenantId: string): Promise<CredentialStatusResponse> {
  return apiClient.get<CredentialStatusResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials/status`)
}

export function addOrUpdateCredential(tenantId: string, token: string): Promise<MessageResponse> {
  return apiClient.post<MessageResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials`, {
    type: 'Token',
    token,
  })
}

export interface CertificateUpload {
  file: File
  key: File | null
  password: string
}

export async function addOrUpdateCertificate(tenantId: string, { file, key, password }: CertificateUpload): Promise<MessageResponse> {
  const bytes = new Uint8Array(await file.arrayBuffer())
  const body = key
    ? { type: 'Certificate', certificatePem: toPem(bytes), privateKeyPem: await key.text(), certificatePassword: password }
    : { type: 'Certificate', certificateBase64: toBase64(bytes), certificatePassword: password }
  return apiClient.post<MessageResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials`, body)
}

function toBase64(bytes: Uint8Array): string {
  return btoa(Array.from(bytes, (byte) => String.fromCharCode(byte)).join(''))
}

function toPem(bytes: Uint8Array): string {
  const text = new TextDecoder().decode(bytes)
  return text.includes('-----BEGIN') ? text : `-----BEGIN CERTIFICATE-----
${toBase64(bytes)}
-----END CERTIFICATE-----
`
}

export function deleteCredential(tenantId: string): Promise<void> {
  return apiClient.delete<void>(`/tenants/${encodeURIComponent(tenantId)}/credentials`)
}

export function forceCredentialSync(tenantId: string): Promise<TenantManualSyncResponse> {
  return apiClient.post<TenantManualSyncResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials/sync`)
}

export function forceFullResync(tenantId: string): Promise<TenantManualSyncResponse> {
  return apiClient.post<TenantManualSyncResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials/full-resync`)
}
