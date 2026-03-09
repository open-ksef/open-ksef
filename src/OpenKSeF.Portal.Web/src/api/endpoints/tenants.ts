import { apiClient } from '../client'
import type { CreateTenantRequest, TenantResponse, UpdateTenantRequest } from '../types'

export function listTenants(): Promise<TenantResponse[]> {
  return apiClient.get<TenantResponse[]>('/tenants')
}

export function getTenant(id: string): Promise<TenantResponse> {
  return apiClient.get<TenantResponse>(`/tenants/${encodeURIComponent(id)}`)
}

export function createTenant(request: CreateTenantRequest): Promise<TenantResponse> {
  return apiClient.post<TenantResponse>('/tenants', request)
}

export function updateTenant(id: string, request: UpdateTenantRequest): Promise<TenantResponse> {
  return apiClient.put<TenantResponse>(`/tenants/${encodeURIComponent(id)}`, request)
}

export function deleteTenant(id: string): Promise<void> {
  return apiClient.delete<void>(`/tenants/${encodeURIComponent(id)}`)
}
