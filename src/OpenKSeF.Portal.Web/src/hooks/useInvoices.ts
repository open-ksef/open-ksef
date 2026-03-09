import { useQuery } from '@tanstack/react-query'

import { listInvoices, type ListInvoicesParams } from '@/api/endpoints/invoices'

export function useInvoices(tenantId: string, params?: ListInvoicesParams, enabled = true) {
  return useQuery({
    queryKey: ['invoices', tenantId, params],
    queryFn: () => listInvoices(tenantId, params),
    enabled: enabled && Boolean(tenantId),
  })
}
