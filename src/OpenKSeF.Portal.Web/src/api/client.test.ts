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

  it('returns a file blob with the Content-Disposition file name', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response('<Faktura/>', {
        status: 200,
        headers: {
          'Content-Type': 'application/xml',
          'Content-Disposition': "attachment; filename=KSEF-1.xml; filename*=UTF-8''KSEF-1.xml",
        },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const file = await new ApiClient('https://example.test').getFile('/x')

    expect(file.fileName).toBe('KSEF-1.xml')
    expect(await file.blob.text()).toBe('<Faktura/>')
  })
})
