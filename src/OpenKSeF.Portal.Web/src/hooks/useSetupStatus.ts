import { useQuery } from '@tanstack/react-query'

import { getSetupStatus } from '@/api/endpoints/system'

export function useSetupStatus() {
  return useQuery({
    queryKey: ['setup-status'],
    queryFn: getSetupStatus,
    staleTime: 60_000,
    retry: 1,
  })
}
