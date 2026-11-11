import { describe, expect, it } from 'vitest'

import type { VatRateCode } from '@/api/types'
import { isValidNip, nipError } from './nip'
import { computeTotals } from './totals'
import vectors from './TotalsVectors.json'

describe('computeTotals matches TotalsVectors.json', () => {
  it.each(vectors)('$name', ({ lines, expected }) => {
    const result = computeTotals(lines.map((line) => ({ ...line, vatRate: line.vatRate as VatRateCode })))
    expect(result).toEqual(expected)
  })
})

describe('isValidNip', () => {
  it('accepts a correct checksum and rejects a wrong one', () => {
    expect(isValidNip('5260250274')).toBe(true)
    expect(isValidNip('1234567890')).toBe(false)
    expect(isValidNip('526025027')).toBe(false)
  })

  it('rejects a NIP whose weighted sum mod 11 is 10', () => {
    expect(isValidNip('1000000000')).toBe(false)
  })

  it('gives one message for length and one for checksum', () => {
    expect(nipError('5260250274')).toBeNull()
    expect(nipError('526025027')).toBe('NIP musi zawierać dokładnie 10 cyfr')
    expect(nipError('1234567890')).toBe('Niepoprawny NIP. Wpisz 10 cyfr z poprawną sumą kontrolną.')
  })
})
