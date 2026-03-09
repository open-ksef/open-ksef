import { useQuery } from '@tanstack/react-query'

import { getOnboardingStatus } from '@/api/endpoints/account'

export function useOnboardingStatus(enabled = true) {
  return useQuery({
    queryKey: ['onboarding-status'],
    queryFn: getOnboardingStatus,
    enabled,
    staleTime: 30_000,
  })
}
