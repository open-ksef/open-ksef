import { describe, expect, it } from 'vitest'

import { formatDateTime, formatDecimal, formatMoney, formatNrb, formatWarsawDate, polishPlural } from './format'
import { vatRateLabel } from './totals'

const plain = (text: string): string => text.replace(/\s/g, ' ')

describe('formatMoney', () => {
  it.each([
    [1476, 'PLN', '1 476,00 PLN'],
    [999, 'PLN', '999,00 PLN'],
    [12345, 'PLN', '12 345,00 PLN'],
    [1248730.55, 'PLN', '1 248 730,55 PLN'],
    [-1476, 'PLN', '-1 476,00 PLN'],
    [1476, 'EUR', '1 476,00 EUR'],
  ])('%d %s reads %s', (amount, currency, expected) => {
    expect(plain(formatMoney(amount, currency))).toBe(expected)
  })
})

describe('formatDecimal', () => {
  it.each([
    [1476, 0, '1 476'],
    [1.5, 0, '1,5'],
    [1476, 2, '1 476,00'],
    [0.123456, 2, '0,123456'],
  ])('%d with at least %d places reads %s', (value, places, expected) => {
    expect(plain(formatDecimal(value, places))).toBe(expected)
  })
})

describe('formatNrb', () => {
  const grouped = '12 3456 7890 1234 5678 9012 3456'

  it.each([
    ['raw', '12345678901234567890123456', grouped],
    ['spaced', grouped, grouped],
    ['with PL', 'PL12345678901234567890123456', grouped],
    ['with PL and spaces', 'PL 12 3456 7890 1234 5678 9012 3456', grouped],
    ['foreign, unchanged', 'DE89 3704 0044 0532 0130 00', 'DE89 3704 0044 0532 0130 00'],
    ['too short, unchanged', '1234', '1234'],
    ['null', null, '—'],
  ])('%s', (_, value, expected) => {
    expect(formatNrb(value)).toBe(expected)
  })
})

describe('formatDateTime', () => {
  it('pads day and month', () => {
    expect(formatDateTime('2026-11-01T06:00:00')).toBe('01.11.2026, 06:00')
  })

  it('shows a dash when empty', () => {
    expect(formatDateTime(null)).toBe('—')
  })
})

describe('formatWarsawDate', () => {
  it.each([
    ['2026-11-02T23:30:00Z', '03.11.2026'],
    ['2026-06-30T22:30:00Z', '01.07.2026'],
    ['2026-11-02T22:59:59Z', '02.11.2026'],
  ])('%s is %s in Warsaw', (iso, expected) => {
    expect(formatWarsawDate(iso)).toBe(expected)
  })

  it('shows a dash when empty', () => {
    expect(formatWarsawDate(null)).toBe('—')
  })
})

describe('vatRateLabel', () => {
  it.each([
    ['23', '23%'],
    ['22', '22%'],
    ['7', '7%'],
    ['0', '0%'],
    ['zw', 'zw'],
    ['0 KR', '0% (kraj)'],
    ['np I', 'np I'],
    ['oo', 'oo'],
  ])('%s reads %s', (code, expected) => {
    expect(vatRateLabel(code)).toBe(expected)
  })
})

describe('polishPlural', () => {
  it.each([
    [0, 'faktur'],
    [1, 'faktura'],
    [2, 'faktury'],
    [4, 'faktury'],
    [5, 'faktur'],
    [12, 'faktur'],
    [14, 'faktur'],
    [21, 'faktur'],
    [22, 'faktury'],
    [112, 'faktur'],
    [1024, 'faktury'],
  ])('%i -> %s', (count, word) => {
    expect(polishPlural(count, ['faktura', 'faktury', 'faktur'])).toBe(word)
  })
})
