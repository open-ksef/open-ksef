import { describe, expect, it } from 'vitest'

import type { VatRateCode } from '@/api/types'
import { computeCorrection } from './correction'
import vectors from './CorrectionVectors.json'

describe('computeCorrection matches CorrectionVectors.json', () => {
  it.each(vectors)('$name', ({ state, after, expected }) => {
    const rate = <T extends { vatRate: string }>(line: T) => ({ ...line, vatRate: line.vatRate as VatRateCode })
    expect(computeCorrection(state.map(rate), after.map(rate))).toEqual(expected)
  })
})
