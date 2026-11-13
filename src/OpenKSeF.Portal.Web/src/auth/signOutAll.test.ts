import { beforeEach, describe, expect, it, vi } from 'vitest'

import { listDevices, unregisterDevice } from '@/api/endpoints/devices'
import { setAccessTokenProvider, setSessionHandlers } from './accessToken'
import { signOutAllDevices } from './signOutAll'

vi.mock('@/api/endpoints/devices', () => ({ listDevices: vi.fn(), unregisterDevice: vi.fn() }))
vi.mock('./config', () => ({ oidcConfig: { authority: '/auth/realms/openksef' } }))

describe('signOutAllDevices', () => {
  const calls: string[] = []
  const end = vi.fn(async () => {
    calls.push('end')
  })
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    calls.length = 0
    vi.clearAllMocks()
    setAccessTokenProvider(() => 'user-token')
    setSessionHandlers({ renew: async () => null, end })
    vi.mocked(listDevices).mockResolvedValue([{ id: 'd-1' }, { id: 'd-2' }] as never)
    vi.mocked(unregisterDevice).mockImplementation(async (id) => {
      calls.push(`unregister ${id}`)
    })
    fetchMock = vi.fn(async (url: string, init: RequestInit) => {
      const headers = new Headers(init.headers)
      calls.push(`${init.method} ${url} ${headers.get('Accept')} ${headers.get('Authorization')}`)
      return new Response(null, { status: 204 })
    })
    vi.stubGlobal('fetch', fetchMock)
  })

  it('unregisters devices, revokes mobile consent, ends all sessions, then clears the local user', async () => {
    await signOutAllDevices()

    expect(calls).toEqual([
      'unregister d-1',
      'unregister d-2',
      'DELETE /auth/realms/openksef/account/applications/openksef-mobile/consent application/json Bearer user-token',
      'DELETE /auth/realms/openksef/account/sessions?current=true application/json Bearer user-token',
      'end',
    ])
  })

  it('stops at a failed device unregister and keeps the session', async () => {
    vi.mocked(unregisterDevice).mockRejectedValueOnce(new Error('Serwer jest chwilowo niedostępny.'))

    await expect(signOutAllDevices()).rejects.toThrow('Serwer jest chwilowo niedostępny.')
    expect(fetchMock).not.toHaveBeenCalled()
    expect(end).not.toHaveBeenCalled()
  })

  it('stops when Keycloak refuses the consent revoke and keeps the session', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 405 }))

    await expect(signOutAllDevices()).rejects.toMatchObject({ status: 405 })
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(end).not.toHaveBeenCalled()
  })

  it('fails on a network error at the sessions call and keeps the session', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 })).mockRejectedValueOnce(new TypeError('Failed to fetch'))

    await expect(signOutAllDevices()).rejects.toMatchObject({ status: 503, message: 'Serwer jest chwilowo niedostępny.' })
    expect(end).not.toHaveBeenCalled()
  })
})
