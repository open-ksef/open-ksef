import { useQuery } from '@tanstack/react-query'

import { getDashboardOverview } from '@/api/endpoints/dashboard'

export function useDashboard() {
  return useQuery({
    queryKey: ['dashboard'],
    queryFn: () => getDashboardOverview(),
  })
}
