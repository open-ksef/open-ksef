import { apiClient } from '../client'
import type { SellerProfileRequest, SellerProfileResponse } from '../types'

export function getSellerProfile(tenantId: string): Promise<SellerProfileResponse> {
  return apiClient.get<SellerProfileResponse>(`/tenants/${encodeURIComponent(tenantId)}/seller-profile`)
}

export function updateSellerProfile(tenantId: string, request: SellerProfileRequest): Promise<SellerProfileResponse> {
  return apiClient.put<SellerProfileResponse>(`/tenants/${encodeURIComponent(tenantId)}/seller-profile`, request)
}
