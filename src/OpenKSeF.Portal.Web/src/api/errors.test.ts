import { describe, expect, it } from 'vitest'

import { ApiError, normalizeError, readValidationEnvelope } from './errors'

describe('normalizeError', () => {
  it('maps HTTP response with JSON message', async () => {
    const response = new Response(JSON.stringify({ message: 'Token expired' }), {
      status: 401,
      headers: { 'Content-Type': 'application/json' },
    })

    const error = await normalizeError(response)

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(401)
    expect(error.message).toBe('Token expired')
  })

  it('uses default message when HTTP response has no body', async () => {
    const response = new Response('', { status: 404 })

    const error = await normalizeError(response)

    expect(error.status).toBe(404)
    expect(error.message).toBe('Not Found')
  })

  it('maps network failures to service unavailable', async () => {
    const error = await normalizeError(new TypeError('Failed to fetch'))

    expect(error.status).toBe(503)
    expect(error.message).toBe('Service Unavailable')
  })

  it('maps abort errors to request timeout', async () => {
    const abort = new DOMException('The operation was aborted.', 'AbortError')

    const error = await normalizeError(abort)

    expect(error.status).toBe(408)
    expect(error.message).toBe('Request Timeout')
  })

  it('returns existing ApiError unchanged', async () => {
    const existing = new ApiError('Already normalized', 422)

    const error = await normalizeError(existing)

    expect(error).toBe(existing)
    expect(error.status).toBe(422)
  })
})

describe('readValidationEnvelope', () => {
  it('reads the envelope from a 422 response and ignores other errors', async () => {
    const body = { errors: [{ code: 'INV-VAL-020', field: 'buyer.nip', message: 'NIP' }], warnings: [] }
    const error = await normalizeError(
      new Response(JSON.stringify(body), { status: 422, headers: { 'Content-Type': 'application/json' } }),
    )

    expect(await readValidationEnvelope(error)).toEqual(body)
    expect(await readValidationEnvelope(new ApiError('x', 500))).toBeNull()
    expect(await readValidationEnvelope(await normalizeError(new Response('', { status: 400 })))).toBeNull()
  })
})
