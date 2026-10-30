import { describe, expect, it } from 'vitest'

import type { VatRateCode } from '@/api/types'
import { isValidNip } from './nip'
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
})
