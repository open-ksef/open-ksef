import { listDevices, unregisterDevice } from '@/api/endpoints/devices'
import { normalizeError } from '@/api/errors'
import { endSession, getAccessToken } from './accessToken'
import { oidcConfig } from './config'

const KEYCLOAK_ACCOUNT_CALLS = ['/account/applications/openksef-mobile/consent', '/account/sessions?current=true']

export async function signOutAllDevices(): Promise<void> {
  for (const device of await listDevices()) {
    await unregisterDevice(device.id)
  }

  const headers = { Accept: 'application/json', Authorization: `Bearer ${getAccessToken()}` }
  for (const path of KEYCLOAK_ACCOUNT_CALLS) {
    const response = await fetch(`${oidcConfig.authority}${path}`, { method: 'DELETE', headers }).catch(async (error: unknown) => {
      throw await normalizeError(error)
    })
    if (!response.ok) throw await normalizeError(response)
  }

  await endSession()
}
