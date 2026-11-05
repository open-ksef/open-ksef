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

export function deleteCredential(tenantId: string): Promise<void> {
  return apiClient.delete<void>(`/tenants/${encodeURIComponent(tenantId)}/credentials`)
}

export function forceCredentialSync(tenantId: string): Promise<TenantManualSyncResponse> {
  return apiClient.post<TenantManualSyncResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials/sync`)
}

export function forceFullResync(tenantId: string): Promise<TenantManualSyncResponse> {
  return apiClient.post<TenantManualSyncResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials/full-resync`)
}
