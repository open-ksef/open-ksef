import { normalizeError } from '@/api/errors'
import { getAccessToken } from './accessToken'
import { oidcConfig } from './config'

export interface AccountCredential {
  id: string
  type: string
  createdDate?: number
  credentialData?: string
}

export interface AccountCredentialType {
  type: string
  userCredentialMetadatas: { credential: AccountCredential }[]
}

export interface AccountSession {
  id: string
  ipAddress: string
  started: number
  lastAccess: number
  browser?: string
  current?: boolean
  clients: { clientId: string; clientName?: string }[]
}

export interface AccountDevice {
  id: string
  ipAddress: string
  os?: string
  browser?: string
  lastAccess: number
  current?: boolean
  mobile?: boolean
  sessions: AccountSession[]
}

export interface LinkedAccount {
  providerAlias: string
  connected: boolean
}

export async function accountFetch(path: string, method = 'GET'): Promise<Response> {
  const response = await fetch(`${oidcConfig.authority}/account${path}`, {
    method,
    headers: { Accept: 'application/json', Authorization: `Bearer ${getAccessToken()}` },
  }).catch(async (error: unknown) => {
    throw await normalizeError(error)
  })
  if (!response.ok) throw await normalizeError(response)
  return response
}

export async function getCredentials(): Promise<AccountCredentialType[]> {
  return (await accountFetch('/credentials')).json() as Promise<AccountCredentialType[]>
}

export async function getSessionDevices(): Promise<AccountDevice[]> {
  return (await accountFetch('/sessions/devices')).json() as Promise<AccountDevice[]>
}

export async function getLinkedAccounts(): Promise<LinkedAccount[]> {
  return (await accountFetch('/linked-accounts')).json() as Promise<LinkedAccount[]>
}

export async function deleteCredential(id: string): Promise<void> {
  await accountFetch(`/credentials/${encodeURIComponent(id)}`, 'DELETE')
}

export function credentialsOf(types: AccountCredentialType[] | undefined, type: string): AccountCredential[] {
  return types?.find((t) => t.type === type)?.userCredentialMetadatas.map((m) => m.credential) ?? []
}

export function remainingRecoveryCodes(types: AccountCredentialType[] | undefined): number | null {
  const [codes] = credentialsOf(types, 'recovery-authn-codes')
  if (!codes?.credentialData) return null
  return (JSON.parse(codes.credentialData) as { remainingCodes?: number }).remainingCodes ?? null
}
