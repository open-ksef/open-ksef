import { describe, expect, it } from 'vitest'

import { advanceDelta, exceedsAdvances, orderValue, remainingAfterAdvances, splitAdvance } from './advance'

const order = [{ quantity: 1, unitNetPrice: 1000, vatRate: '23' as const }]

describe('advance split (REQ-0028)', () => {
  it('369,00 of a 1 230,00 order is 300,00 net and 69,00 VAT', () => {
    expect(orderValue(order)).toBe(1230)
    expect(splitAdvance(order, 369)).toEqual({ rows: [{ vatRate: '23', net: 300, vat: 69 }], totalNet: 300, totalVat: 69, totalGross: 369 })
    expect(splitAdvance(order, 246).rows).toEqual([{ vatRate: '23', net: 200, vat: 46 }])
  })

  it('115,50 over 23% and 8% splits proportionally to gross', () => {
    const mixed = [
      { quantity: 1, unitNetPrice: 100, vatRate: '23' as const },
      { quantity: 1, unitNetPrice: 100, vatRate: '8' as const },
    ]
    expect(splitAdvance(mixed, 115.5).rows).toEqual([
      { vatRate: '23', net: 50, vat: 11.5 },
      { vatRate: '8', net: 50, vat: 4 },
    ])
  })

  it('the rounding remainder goes to the largest share and np has no VAT', () => {
    const mixed = [
      { quantity: 1, unitNetPrice: 200, vatRate: 'np I' as const },
      { quantity: 1, unitNetPrice: 100, vatRate: 'np I' as const },
      { quantity: 1, unitNetPrice: 0.01, vatRate: 'zw' as const },
    ]
    const split = splitAdvance(mixed, 100)
    expect(split.totalGross).toBe(100)
    expect(split.rows.find((row) => row.vatRate === 'np I')).toEqual({ vatRate: 'np I', net: 100, vat: 0 })
  })

  it('ROZ remaining per bucket deducts advances; zero is allowed, below zero is blocked', () => {
    const paid = [
      { vatRate: '23' as const, net: 300, vat: 69 },
      { vatRate: '23' as const, net: 200, vat: 46 },
    ]
    const remaining = remainingAfterAdvances(order, paid)
    expect(remaining).toEqual({ rows: [{ vatRate: '23', net: 500, vat: 115 }], totalNet: 500, totalVat: 115, totalGross: 615 })
    expect(exceedsAdvances(remaining)).toBe(false)
    expect(exceedsAdvances(remainingAfterAdvances([{ ...order[0], unitNetPrice: 500 }], paid))).toBe(false)
    expect(exceedsAdvances(remainingAfterAdvances([{ ...order[0], unitNetPrice: 400 }], paid))).toBe(true)
  })

  it('KOR_ZAL 369,00 to 246,00 gives -100,00 net, -23,00 VAT, -123,00; to zero reverses the advance', () => {
    expect(advanceDelta(order, order, 369, 246)).toEqual({ rows: [{ vatRate: '23', net: -100, vat: -23 }], totalNet: -100, totalVat: -23, totalGross: -123 })
    expect(advanceDelta(order, [], 369, 0).totalGross).toBe(-369)
  })
})
