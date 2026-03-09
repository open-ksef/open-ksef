import { useQuery } from '@tanstack/react-query'

import { listTenants } from '@/api/endpoints/tenants'

export function useTenants() {
  return useQuery({
    queryKey: ['tenants'],
    queryFn: () => listTenants(),
  })
}
