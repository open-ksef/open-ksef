import type { TenantDashboardSummaryResponse } from '@/api/types'
import { formatDateTime } from '@/invoicing/format'

export const NO_CREDENTIAL_REASON = 'Bez tokenu nie pobierzemy faktur ani nie wystawimy nowych.'

export interface SyncHealth {
  problem: 'NoCredential' | 'NeverSynced' | 'Failed' | 'Stale'
  headline: string
  reason: string | null
  link: 'add' | 'check' | null
}

export function syncHealth(summary: TenantDashboardSummaryResponse | undefined): SyncHealth | null {
  if (!summary) return null
  const since = summary.lastSuccessfulSync ? `Nie pobieramy faktur od ${formatDateTime(summary.lastSuccessfulSync)}` : null
  switch (summary.syncProblem) {
    case 'NoCredential':
      return { problem: 'NoCredential', headline: 'Ta firma nie ma tokenu KSeF', reason: NO_CREDENTIAL_REASON, link: 'add' }
    case 'NeverSynced':
      return {
        problem: 'NeverSynced',
        headline: 'Faktury nie były jeszcze pobierane',
        reason: 'Pierwsze pobranie ruszy samo w ciągu kilku godzin. Możesz też kliknąć „Pobierz z KSeF”.',
        link: null,
      }
    case 'Failed': {
      const reason = summary.lastSyncErrorMessage ?? null
      return {
        problem: 'Failed',
        headline: since ?? 'Nie udało się jeszcze pobrać faktur',
        reason,
        link: reason === null || summary.lastSyncErrorCredentialCause ? 'check' : null,
      }
    }
    default:
      return since && summary.syncStatus !== 'Success' ? { problem: 'Stale', headline: since, reason: null, link: 'check' } : null
  }
}
