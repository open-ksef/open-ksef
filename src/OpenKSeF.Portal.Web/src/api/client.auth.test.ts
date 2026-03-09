import { beforeEach, describe, expect, it, vi } from 'vitest'

import { apiClient } from './client'
import { clearAccessTokenProvider, setAccessTokenProvider } from '@/auth/accessToken'

describe('apiClient auth integration', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    clearAccessTokenProvider()
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
})
