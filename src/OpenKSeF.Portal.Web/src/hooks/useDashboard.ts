import { useQuery, type UseQueryOptions } from '@tanstack/react-query'

import { getDashboardOverview } from '@/api/endpoints/dashboard'
import type { TenantDashboardSummaryResponse } from '@/api/types'

type RefetchInterval = UseQueryOptions<TenantDashboardSummaryResponse[]>['refetchInterval']

export function useDashboard(refetchInterval?: RefetchInterval) {
  return useQuery({
    queryKey: ['dashboard'],
    queryFn: () => getDashboardOverview(),
    refetchInterval,
  })
}
