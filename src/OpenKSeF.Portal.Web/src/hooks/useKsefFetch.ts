import { useMutation, useMutationState, useQueryClient } from '@tanstack/react-query'
import toast from 'react-hot-toast'

import { forceCredentialSync } from '@/api/endpoints/credentials'
import { ApiError } from '@/api/errors'
import { companyName } from '@/company/CompanyContext'
import { useCompany } from '@/company/useCompany'

const MUTATION_KEY = ['ksef-fetch']
const FETCH_NOT_STARTED = 'Nie uruchomiono pobierania z KSeF.'
const FETCH_OUTCOME_UNKNOWN = 'Nie mamy potwierdzenia pobrania z KSeF. Część faktur mogła się pobrać. Sprawdź listę i w razie potrzeby spróbuj ponownie.'
const FETCH_FAILED = 'Pobieranie z KSeF nie powiodło się. Spróbuj ponownie później. Jeśli błąd wraca, sprawdź token KSeF.'

function isMissingToken(error: Error | null): boolean {
  return error instanceof ApiError && error.status === 409
}

function fetchErrorMessage(error: Error): string {
  if (error instanceof ApiError && [401, 403, 404, 429].includes(error.status)) return FETCH_NOT_STARTED
  const syncFailed =
    error instanceof ApiError &&
    error.status === 502 &&
    error.originalError instanceof Response &&
    error.originalError.headers.get('Content-Type')?.includes('json')
  return syncFailed ? FETCH_FAILED : FETCH_OUTCOME_UNKNOWN
}

export interface KsefFetch {
  pending: boolean
  fetched: { fetchedInvoices: number; newInvoices: number } | null
  missingToken: boolean
  start: () => void
}

export function useKsefFetch(tenantId: string): KsefFetch {
  const queryClient = useQueryClient()
  const { tenants } = useCompany()

  const mutation = useMutation({
    mutationKey: MUTATION_KEY,
    mutationFn: (id: string) => forceCredentialSync(id),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      void queryClient.invalidateQueries({ queryKey: ['invoices'] })
    },
    onError: (error, id) => {
      if (isMissingToken(error)) return
      const company = tenants.find((tenant) => tenant.id === id)
      toast.error(company ? `${companyName(company)}: ${fetchErrorMessage(error)}` : fetchErrorMessage(error))
    },
  })

  // useIsMutating with a tenant predicate keeps its last count until the next mutation event, so a company switch would read stale.
  const pendingTenants = useMutationState({
    filters: { mutationKey: MUTATION_KEY, status: 'pending' },
    select: (running) => running.state.variables,
  })

  return {
    pending: pendingTenants.includes(tenantId),
    fetched: mutation.data?.tenantId === tenantId ? mutation.data : null,
    missingToken: isMissingToken(mutation.error) && mutation.variables === tenantId,
    start: () => mutation.mutate(tenantId),
  }
}
