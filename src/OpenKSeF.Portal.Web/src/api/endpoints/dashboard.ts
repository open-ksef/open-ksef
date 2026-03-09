import { apiClient } from '../client'
import type { TenantDashboardSummaryResponse } from '../types'

export function getDashboardOverview(): Promise<TenantDashboardSummaryResponse[]> {
  return apiClient.get<TenantDashboardSummaryResponse[]>('/dashboard')
}
