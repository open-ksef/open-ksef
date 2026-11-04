import { describe, expect, it } from 'vitest'

import { amountInWords } from './amountWords'

describe('amountInWords', () => {
  it.each([
    ['0.50', 'GBP', 'zero funtów 50/100', 'zero British pounds 50/100'],
    ['1.00', 'GBP', 'jeden funt 00/100', 'one British pound 00/100'],
    ['2.00', 'GBP', 'dwa funty 00/100', 'two British pounds 00/100'],
    ['12.00', 'GBP', 'dwanaście funtów 00/100', 'twelve British pounds 00/100'],
    ['21.00', 'GBP', 'dwadzieścia jeden funtów 00/100', 'twenty-one British pounds 00/100'],
    ['22.00', 'GBP', 'dwadzieścia dwa funty 00/100', 'twenty-two British pounds 00/100'],
    ['101.00', 'USD', 'sto jeden dolarów 00/100', 'one hundred and one US dollars 00/100'],
    ['1000.00', 'GBP', 'tysiąc funtów 00/100', 'one thousand British pounds 00/100'],
    ['1002.00', 'EUR', 'tysiąc dwa euro 00/100', 'one thousand and two euro 00/100'],
    ['2350.50', 'GBP', 'dwa tysiące trzysta pięćdziesiąt funtów 50/100', 'two thousand three hundred and fifty British pounds 50/100'],
    ['5000.00', 'CHF', 'pięć tysięcy franków 00/100', 'five thousand Swiss francs 00/100'],
    ['22000.00', 'GBP', 'dwadzieścia dwa tysiące funtów 00/100', 'twenty-two thousand British pounds 00/100'],
    ['1000000.00', 'GBP', 'milion funtów 00/100', 'one million British pounds 00/100'],
    ['100.50', 'CAD', 'sto CAD 50/100', 'one hundred CAD 50/100'],
  ])('REQ-0030 table: %s %s', (amount, currency, pl, en) => {
    expect(amountInWords(amount, currency)).toEqual({ pl, en })
  })

  it('uses the Polish plural of the last digits and the PLN names', () => {
    expect(amountInWords('2.00', 'PLN')).toEqual({ pl: 'dwa złote 00/100', en: 'two Polish zlotys 00/100' })
    expect(amountInWords('1.00', 'PLN')?.pl).toBe('jeden złoty 00/100')
    expect(amountInWords('113.00', 'USD')?.pl).toBe('sto trzynaście dolarów 00/100')
    expect(amountInWords('1001.00', 'USD')?.pl).toBe('tysiąc jeden dolarów 00/100')
    expect(amountInWords('21000.00', 'USD')?.pl).toBe('dwadzieścia jeden tysięcy dolarów 00/100')
  })

  it('reaches 999 999 999 999.99 and leaves larger amounts out', () => {
    const largest = amountInWords('999999999999.99', 'GBP')
    expect(largest?.pl.startsWith('dziewięćset dziewięćdziesiąt dziewięć miliardów')).toBe(true)
    expect(largest?.en.startsWith('nine hundred and ninety-nine billion')).toBe(true)
    expect(amountInWords('1000000000000.00', 'GBP')).toBeNull()
    expect(amountInWords('1000000000.00', 'GBP')?.pl).toBe('miliard funtów 00/100')
  })

  it('keeps "and" only before a last group below one hundred', () => {
    expect(amountInWords('1000500.00', 'GBP')?.en).toBe('one million five hundred British pounds 00/100')
    expect(amountInWords('1000001.00', 'GBP')?.en).toBe('one million and one British pounds 00/100')
  })
})
