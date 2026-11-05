import { apiClient } from '../client'
import type { NipLookupResponse } from '../types'

export function lookupNip(nip: string): Promise<NipLookupResponse> {
  return apiClient.get<NipLookupResponse>(`/nip-lookup/${encodeURIComponent(nip)}`)
}
