import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiClient, apiClient } from './client'
import { clearAccessTokenProvider, clearSessionHandlers, setAccessTokenProvider, setSessionHandlers } from '@/auth/accessToken'

describe('apiClient auth integration', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    clearAccessTokenProvider()
    clearSessionHandlers()
  })

  it('injects Authorization header from shared access token provider', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    setAccessTokenProvider(() => 'shared-token')

    await apiClient.get('/me')

    const [, init] = fetchMock.mock.calls[0]
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBe('Bearer shared-token')
  })

  it('does not set Authorization when provider returns null', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    setAccessTokenProvider(() => null)

    await apiClient.get('/me')

    const [, init] = fetchMock.mock.calls[0]
    const headers = new Headers(init?.headers)
    expect(headers.get('Authorization')).toBeNull()
  })

  const challenge = () => new Response('', { status: 401, headers: { 'WWW-Authenticate': 'Bearer error="invalid_token"' } })
  const ok = () => new Response(JSON.stringify({ ok: true }), { status: 200, headers: { 'Content-Type': 'application/json' } })
  const authOf = (init?: RequestInit) => new Headers(init?.headers).get('Authorization')

  function session(renewed: string | null) {
    const handlers = { renew: vi.fn().mockResolvedValue(renewed), end: vi.fn().mockResolvedValue(undefined) }
    setSessionHandlers(handlers)
    setAccessTokenProvider(() => 'old-token')
    return handlers
  }

  it('renews once on a Bearer challenge and retries with the new token and the same body', async () => {
    const sent: (string | null)[] = []
    const responses = [challenge(), ok()]
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      sent.push(authOf(init))
      return responses.shift()!
    })
    vi.stubGlobal('fetch', fetchMock)
    const handlers = session('new-token')

    await expect(apiClient.post('/tenants', { nip: '1' })).resolves.toEqual({ ok: true })

    expect(handlers.renew).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(sent).toEqual(['Bearer old-token', 'Bearer new-token'])
    expect(fetchMock.mock.calls[1][1]?.body).toBe('{"nip":"1"}')
    expect(handlers.end).not.toHaveBeenCalled()
  })

  it('throws the original 401 without a retry when the renew gives no token', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(challenge())
    vi.stubGlobal('fetch', fetchMock)
    session(null)

    await expect(apiClient.get('/me')).rejects.toMatchObject({ status: 401, message: 'Sesja wygasła. Zaloguj się ponownie.' })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('ends the session when the retry is challenged again, with no third request', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(challenge()).mockResolvedValueOnce(challenge())
    vi.stubGlobal('fetch', fetchMock)
    const handlers = session('new-token')

    await expect(apiClient.get('/me')).rejects.toMatchObject({ status: 401 })
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(handlers.end).toHaveBeenCalledTimes(1)
  })

  it('does not renew on a 401 without a challenge, such as a wrong Keycloak admin password in Settings', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ error: 'Nieprawidłowe dane administratora Keycloak.' }), {
      status: 401,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)
    const handlers = session('new-token')

    await expect(apiClient.post('/system/settings', {})).rejects.toMatchObject({ status: 401, message: 'Nieprawidłowe dane administratora Keycloak.' })
    expect(handlers.renew).not.toHaveBeenCalled()
    expect(handlers.end).not.toHaveBeenCalled()
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('a client without a renewer keeps a challenged 401 as is', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(challenge())
    vi.stubGlobal('fetch', fetchMock)
    const handlers = session('new-token')

    await expect(new ApiClient('/api').get('/system/setup-status')).rejects.toMatchObject({ status: 401 })
    expect(handlers.renew).not.toHaveBeenCalled()
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})
