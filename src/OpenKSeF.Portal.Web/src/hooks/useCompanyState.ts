import type { TenantCredentialStatusResponse, TenantDashboardSummaryResponse } from '@/api/types'
import { useCompany } from '@/company/useCompany'
import { useCredentials } from './useCredentials'
import { useDashboard } from './useDashboard'
import { useTenants } from './useTenants'

export type CompanyState = 'no-token' | 'first-fetch' | 'first-fetch-late' | 'zero' | 'full'

const FIRST_FETCH_LATE_MS = 24 * 60 * 60 * 1000
export const FIRST_FETCH_POLL_MS = 60_000

export function companyState(
  summary: TenantDashboardSummaryResponse | undefined,
  credential: TenantCredentialStatusResponse | undefined,
  now: number,
): CompanyState | null {
  if (!summary || !credential) return null
  if (summary.totalInvoices > 0) return 'full'
  if (!credential.hasCredential) return 'no-token'
  if (summary.lastSuccessfulSync) return 'zero'
  return now - Date.parse(credential.lastUpdatedAt ?? '') < FIRST_FETCH_LATE_MS ? 'first-fetch' : 'first-fetch-late'
}

export function useCompanyState() {
  const { tenantId } = useCompany()
  const tenants = useTenants()
  const credentials = useCredentials()
  const credential = credentials.data?.find((row) => row.tenantId === tenantId)

  const findSummary = (rows: TenantDashboardSummaryResponse[] | undefined) => rows?.find((row) => row.tenantId === tenantId)
  const dashboard = useDashboard((query) =>
    companyState(findSummary(query.state.data), credential, query.state.dataUpdatedAt) === 'first-fetch' ? FIRST_FETCH_POLL_MS : false,
  )
  const summary = findSummary(dashboard.data)
  const queries = [tenants, credentials, dashboard]

  return {
    state: companyState(summary, credential, dashboard.dataUpdatedAt),
    summary,
    summaries: dashboard.data ?? [],
    credential,
    isLoading: queries.some((query) => query.isLoading) || ((!summary || !credential) && queries.some((query) => query.isFetching)),
    error: queries.find((query) => query.isLoadingError)?.error ?? null,
    refetch: () => queries.forEach((query) => void query.refetch()),
  }
}
