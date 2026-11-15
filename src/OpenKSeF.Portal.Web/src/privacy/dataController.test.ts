import { describe, expect, it } from 'vitest'

import { dataControllerError, hasAdminRole, isEmailContact } from './dataController'

describe('isEmailContact', () => {
  it.each([
    ['rodo@example.com', true],
    ['ul. Przykładowa 1, 00-001 Warszawa', false],
    ['rodo@example.com lub telefon', false],
    ['rodo @example.com', false],
  ])('%s -> %s', (contact, expected) => {
    expect(isEmailContact(contact)).toBe(expected)
  })
})

describe('dataControllerError', () => {
  it('accepts values at the limit after trim', () => {
    expect(dataControllerError(` ${'n'.repeat(200)} `, ` ${'c'.repeat(300)} `)).toBeNull()
  })
})

describe('hasAdminRole', () => {
  const token = (payload: object) => `h.${btoa(JSON.stringify(payload)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_')}.s`

  it('reads openksef-admin from realm roles', () => {
    expect(hasAdminRole(token({ realm_access: { roles: ['openksef-admin'] } }))).toBe(true)
    expect(hasAdminRole(token({ realm_access: { roles: ['user'] } }))).toBe(false)
  })

  it('is false for a missing or broken token', () => {
    expect(hasAdminRole(undefined)).toBe(false)
    expect(hasAdminRole('not-a-jwt')).toBe(false)
  })
})
