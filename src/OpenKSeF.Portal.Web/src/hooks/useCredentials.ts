import { useQuery } from '@tanstack/react-query'

import { listCredentials } from '@/api/endpoints/credentials'

export function useCredentials() {
  return useQuery({
    queryKey: ['credentials'],
    queryFn: () => listCredentials(),
  })
}
