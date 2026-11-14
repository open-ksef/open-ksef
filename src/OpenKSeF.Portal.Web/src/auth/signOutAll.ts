import { listDevices, unregisterDevice } from '@/api/endpoints/devices'
import { accountFetch } from './account'
import { endSession } from './accessToken'

const KEYCLOAK_ACCOUNT_CALLS = ['/applications/openksef-mobile/consent', '/sessions?current=true']

export async function signOutAllDevices(): Promise<void> {
  for (const device of await listDevices()) {
    await unregisterDevice(device.id)
  }

  for (const path of KEYCLOAK_ACCOUNT_CALLS) {
    await accountFetch(path, 'DELETE')
  }

  await endSession()
}
