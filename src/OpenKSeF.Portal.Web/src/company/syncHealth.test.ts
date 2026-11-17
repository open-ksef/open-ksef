import { describe, expect, it } from 'vitest'

import type { TenantDashboardSummaryResponse } from '@/api/types'
import { syncHealth } from './syncHealth'

const row = (overrides: Partial<TenantDashboardSummaryResponse> = {}): TenantDashboardSummaryResponse => ({
  tenantId: 'a',
  nip: '1111111111',
  displayName: 'A',
  lastSyncedAt: null,
  lastSuccessfulSync: '2026-11-03T06:00:00',
  totalInvoices: 1,
  invoicesLast7Days: 0,
  invoicesLast30Days: 0,
  syncStatus: 'Success',
  syncProblem: null,
  ...overrides,
})
const failed = (category: string, message: string | null, credentialCause: boolean, lastSuccessfulSync: string | null = '2026-11-03T06:00:00') =>
  row({ syncProblem: 'Failed', syncStatus: 'Warning', lastSyncErrorCategory: category, lastSyncErrorMessage: message, lastSyncErrorCredentialCause: credentialCause, lastSuccessfulSync })

describe('syncHealth (REQ-0013)', () => {
  it('healthy sync has nothing to say, also after a failure was cleared', () => {
    expect(syncHealth(row())).toBeNull()
    expect(syncHealth(undefined)).toBeNull()
  })

  it('stale without a recorded failure: headline only, no reason', () => {
    expect(syncHealth(row({ syncStatus: 'Warning' }))).toEqual({ problem: 'Stale', headline: 'Nie pobieramy faktur od 03.11.2026, 06:00', reason: null, link: null })
  })

  it('NoCredential', () => {
    expect(syncHealth(row({ syncProblem: 'NoCredential', syncStatus: 'Error' }))).toEqual({
      problem: 'NoCredential',
      headline: 'Ta firma nie ma tokenu KSeF',
      reason: 'Bez tokenu nie pobierzemy faktur ani nie wystawimy nowych.',
      link: 'add',
    })
  })

  it('NeverSynced has its own text and no link', () => {
    expect(syncHealth(row({ syncProblem: 'NeverSynced', lastSuccessfulSync: null, syncStatus: 'Error' }))).toEqual({
      problem: 'NeverSynced',
      headline: 'Faktury nie były jeszcze pobierane',
      reason: 'Pierwsze pobranie ruszy samo w ciągu kilku godzin. Możesz też kliknąć „Pobierz z KSeF”.',
      link: null,
    })
  })

  it.each([
    ['InvalidToken', true],
    ['InvalidCertificate', true],
    ['LoginRefused', true],
    ['RateLimited', false],
    ['KsefUnavailable', false],
    ['StoreFailed', false],
    ['Other', false],
  ])('Failed %s shows the API text verbatim, token link only when the credential is the cause', (category, cause) => {
    const health = syncHealth(failed(category, `tekst ${category}`, cause))
    expect(health).toEqual({ problem: 'Failed', headline: 'Nie pobieramy faktur od 03.11.2026, 06:00', reason: `tekst ${category}`, link: cause ? 'check' : null })
  })

  it('a non-credential failure while the last success is fresh raises no alarm', () => {
    expect(syncHealth({ ...failed('KsefUnavailable', 'x', false), syncStatus: 'Success' })).toBeNull()
    expect(syncHealth({ ...failed('InvalidToken', 'x', true), syncStatus: 'Success' })?.link).toBe('check')
  })

  it('first-ever failure has its own headline', () => {
    expect(syncHealth(failed('KsefUnavailable', 'x', false, null))?.headline).toBe('Nie udało się jeszcze pobrać faktur')
  })

  it('Failed without a message falls back to headline plus token link', () => {
    expect(syncHealth(failed('Unknown', null, false))).toEqual({ problem: 'Failed', headline: 'Nie pobieramy faktur od 03.11.2026, 06:00', reason: null, link: 'check' })
  })
})
