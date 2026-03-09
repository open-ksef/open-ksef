import { apiClient } from '../client'
import type { CredentialStatusResponse, CredentialType, TenantCredentialStatusResponse, TenantManualSyncResponse } from '../types'

interface MessageResponse {
  message: string
}

export interface AddCredentialPayload {
  type: CredentialType
  token?: string
  certificatePem?: string
  privateKeyPem?: string
  certificateBase64?: string
  certificatePassword?: string
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

export function addOrUpdateCredentialWithType(
  tenantId: string,
  payload: AddCredentialPayload,
): Promise<MessageResponse> {
  return apiClient.post<MessageResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials`, payload)
}

export async function addOrUpdatePemCertificateCredential(
  tenantId: string,
  certFile: File,
  keyFile: File,
  password: string,
): Promise<MessageResponse> {
  const [certPem, keyPem] = await Promise.all([fileToText(certFile), fileToText(keyFile)])
  return addOrUpdateCredentialWithType(tenantId, {
    type: 'Certificate',
    certificatePem: certPem,
    privateKeyPem: keyPem,
    certificatePassword: password || undefined,
  })
}

export async function addOrUpdateCertificateCredential(
  tenantId: string,
  file: File,
  password: string,
): Promise<MessageResponse> {
  const base64 = await fileToBase64(file)
  return addOrUpdateCredentialWithType(tenantId, {
    type: 'Certificate',
    certificateBase64: base64,
    certificatePassword: password,
  })
}

export function deleteCredential(tenantId: string): Promise<void> {
  return apiClient.delete<void>(`/tenants/${encodeURIComponent(tenantId)}/credentials`)
}

export function forceCredentialSync(tenantId: string): Promise<TenantManualSyncResponse> {
  return apiClient.post<TenantManualSyncResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials/sync`)
}

function fileToBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => {
      const result = reader.result as string
      const base64 = result.split(',')[1]
      resolve(base64)
    }
    reader.onerror = reject
    reader.readAsDataURL(file)
  })
}

function fileToText(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = reject
    reader.readAsText(file)
  })
}
