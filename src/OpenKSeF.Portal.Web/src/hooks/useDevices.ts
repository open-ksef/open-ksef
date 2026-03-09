import { useQuery } from '@tanstack/react-query'

import { listDevices } from '@/api/endpoints/devices'

export function useDevices() {
  return useQuery({
    queryKey: ['devices'],
    queryFn: () => listDevices(),
  })
}
