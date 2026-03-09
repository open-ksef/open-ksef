import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiClient } from './client'

describe('ApiClient', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('prepends base URL and appends query parameters', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const client = new ApiClient('https://example.test/api')

    await client.get<{ ok: boolean }>('/tenants', {
      query: {
        page: 2,
        search: 'nip',
        skip: undefined,
      },
    })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url] = fetchMock.mock.calls[0]
    expect(url).toBe('https://example.test/api/tenants?page=2&search=nip')
  })

  it('adds Authorization header when token provider returns token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const client = new ApiClient('https://example.test', {
      getToken: () => 'jwt-token',
    })

    await client.get('/me', {
      headers: {
        'X-Correlation-Id': 'abc123',
      },
    })

    const [, init] = fetchMock.mock.calls[0]
    const headers = new Headers(init?.headers)

    expect(headers.get('Authorization')).toBe('Bearer jwt-token')
    expect(headers.get('X-Correlation-Id')).toBe('abc123')
  })

  it('serializes JSON body for POST requests', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: '1' }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const client = new ApiClient('https://example.test')

    await client.post('/tenants', {
      nip: '1234567890',
    })

    const [, init] = fetchMock.mock.calls[0]
    const headers = new Headers(init?.headers)

    expect(init?.method).toBe('POST')
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(init?.body).toBe(JSON.stringify({ nip: '1234567890' }))
  })

  it('does not override explicitly provided Authorization header', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const client = new ApiClient('https://example.test', {
      getToken: () => 'jwt-token',
    })

    await client.get('/me', {
      headers: {
        Authorization: 'Bearer explicit-token',
      },
    })

    const [, init] = fetchMock.mock.calls[0]
    const headers = new Headers(init?.headers)

    expect(headers.get('Authorization')).toBe('Bearer explicit-token')
  })
})
