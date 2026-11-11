import { apiClient } from '../client'
import { ApiError, normalizeError } from '../errors'
import type { CredentialStatusResponse, TenantCredentialStatusResponse, TenantManualSyncResponse, TenantSyncStatusResponse } from '../types'

interface MessageResponse {
  message: string
}

export function listCredentials(): Promise<TenantCredentialStatusResponse[]> {
  return apiClient.get<TenantCredentialStatusResponse[]>('/credentials')
}

export function getCredentialStatus(tenantId: string): Promise<CredentialStatusResponse> {
  return apiClient.get<CredentialStatusResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials/status`)
}

export function addOrUpdateCredential(tenantId: string, token: string): Promise<MessageResponse> {
  return apiClient.post<MessageResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials`, {
    type: 'Token',
    token,
  })
}

const MAX_REQUEST_BYTES = 64 * 1024

export interface CertificateUpload {
  file: File
  key: File | null
  password: string
}

export async function addOrUpdateCertificate(tenantId: string, { file, key, password }: CertificateUpload): Promise<MessageResponse> {
  const bytes = new Uint8Array(await file.arrayBuffer())
  const body = key
    ? { type: 'Certificate', certificatePem: toPem(bytes), privateKeyPem: await key.text(), certificatePassword: password }
    : { type: 'Certificate', certificateBase64: toBase64(bytes), certificatePassword: password }
  if (new TextEncoder().encode(JSON.stringify(body)).length > MAX_REQUEST_BYTES) throw new Error('Pliki są za duże. Certyfikat i klucz razem mogą mieć najwyżej ok. 47 KB.')
  return apiClient.post<MessageResponse>(`/tenants/${encodeURIComponent(tenantId)}/credentials`, body)
}

function toBase64(bytes: Uint8Array): string {
  return btoa(Array.from(bytes, (byte) => String.fromCharCode(byte)).join(''))
}

function toPem(bytes: Uint8Array): string {
  const text = new TextDecoder().decode(bytes)
  return text.includes('-----BEGIN') ? text : `-----BEGIN CERTIFICATE-----
${toBase64(bytes)}
-----END CERTIFICATE-----
`
}

export function deleteCredential(tenantId: string): Promise<void> {
  return apiClient.delete<void>(`/tenants/${encodeURIComponent(tenantId)}/credentials`)
}

const SYNC_POLL_INTERVAL_MS = 3000
const SYNC_POLL_CAP_MS = 15 * 60 * 1000
const SYNC_ALREADY_RUNNING = 'Trwa już synchronizacja. Uruchom pełną resynchronizację po jej zakończeniu.'

export function forceCredentialSync(tenantId: string): Promise<TenantManualSyncResponse> {
  return runSync(tenantId, 'sync', false)
}

export function forceFullResync(tenantId: string): Promise<TenantManualSyncResponse> {
  return runSync(tenantId, 'full-resync', true)
}

async function runSync(tenantId: string, action: string, fullResync: boolean): Promise<TenantManualSyncResponse> {
  const base = `/tenants/${encodeURIComponent(tenantId)}/credentials`
  let status = await apiClient.post<TenantSyncStatusResponse>(`${base}/${action}`, undefined, { headers: { Prefer: 'respond-async' } })
  if (fullResync && !status.fullResync) throw new ApiError(SYNC_ALREADY_RUNNING, 409)

  const deadline = Date.now() + SYNC_POLL_CAP_MS
  while (status.state === 'waiting' || status.state === 'running') {
    if (Date.now() >= deadline) throw await normalizeError(new Response(null, { status: 408 }))
    await new Promise((resolve) => setTimeout(resolve, SYNC_POLL_INTERVAL_MS))
    try {
      status = await apiClient.get<TenantSyncStatusResponse>(`${base}/sync`)
    } catch (error) {
      if (!(error instanceof ApiError) || !isTransient(error.status)) throw error
    }
  }

  if (status.state === 'succeeded' && status.result) return status.result
  if (status.state !== 'failed') throw await normalizeError(new Response(null, { status: 408 }))
  // A real Response keeps the hooks' error mapping, which reads the status and the JSON content type.
  throw await normalizeError(new Response(JSON.stringify({ error: status.error }), {
    status: status.errorStatus ?? 500,
    headers: { 'Content-Type': 'application/json' },
  }))
}

function isTransient(status: number): boolean {
  return status === 408 || status === 429 || status >= 500
}
