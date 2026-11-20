import { describe, expect, it } from 'vitest'

import type { SellerProfileResponse } from '@/api/types'
import {
  fromProfile,
  isValidNrb,
  previewNumber,
  toSellerProfileRequest,
  validateSellerProfile,
} from './sellerProfile'

const profile: SellerProfileResponse = {
  legalName: 'OpenKSeF Test A sp. z o.o.',
  addressLine1: 'ul. Prosta 1',
  addressLine2: '00-001 Warszawa',
  bankAccount: '93000000002026111700000815',
  cashAccounting: false,
  numberingPattern: 'FV/{nr}/{MM}/{RRRR}',
  nextNumber: 57,
  nextNumberPeriod: '2026-11',
  nip: '4444444444',
  isComplete: true,
  nextNumberPreview: 'FV/57/11/2026',
  ksefEnvironment: 'test',
}

const today = new Date(2026, 10, 3)

describe('seller profile form', () => {
  it('checks NRB with the IBAN mod 97 rule, spaces allowed', () => {
    expect(isValidNrb('93000000002026111700000815')).toBe(true)
    expect(isValidNrb('93 0000 0000 2026 1117 0000 0815')).toBe(true)
    expect(isValidNrb('94000000002026111700000815')).toBe(false)
    expect(isValidNrb('9300000000202611170000081')).toBe(false)
    expect(isValidNrb('PL93000000002026111700000815')).toBe(false)
  })

  it('previews the next number from pattern tokens', () => {
    expect(previewNumber('FV/{nr}/{MM}/{RRRR}', 1, today)).toBe('FV/1/11/2026')
    expect(previewNumber('{RRRR}-{nr}', 12, today)).toBe('2026-12')
  })

  it('reports required fields, bad NRB, missing {nr} and bad next number', () => {
    const errors = validateSellerProfile({
      ...fromProfile(profile),
      legalName: ' ',
      addressLine1: '',
      bankAccount: '1234',
      numberingPattern: 'FV/{MM}',
      nextNumber: '0',
    })
    expect(Object.keys(errors).sort()).toEqual(['addressLine1', 'bankAccount', 'legalName', 'nextNumber', 'numberingPattern'])
    expect(validateSellerProfile(fromProfile(profile))).toEqual({})
    expect(validateSellerProfile({ ...fromProfile(profile), bankAccount: '' })).toEqual({})
  })

  it('sends next number only when changed, with the period of the pattern', () => {
    const unchanged = toSellerProfileRequest({ ...fromProfile(profile), bankAccount: '93 0000 0000 2026 1117 0000 0815' }, profile, today)
    expect(unchanged).not.toHaveProperty('nextNumber')
    expect(unchanged.bankAccount).toBe('93000000002026111700000815')

    const yearly = toSellerProfileRequest(
      { ...fromProfile(profile), numberingPattern: 'FV/{nr}/{RRRR}', nextNumber: '5', addressLine2: '' },
      profile,
      today,
    )
    expect(yearly).toMatchObject({ nextNumber: 5, nextNumberPeriod: '2026', addressLine2: null })
  })
})
