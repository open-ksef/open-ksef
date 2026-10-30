import type { VatRateCode, VatSummaryRow } from '@/api/types'

export const VAT_RATES: readonly { code: VatRateCode; label: string; percent: number }[] = [
  { code: '23', label: '23%', percent: 23 },
  { code: '8', label: '8%', percent: 8 },
  { code: '5', label: '5%', percent: 5 },
  { code: '0 KR', label: '0% (kraj)', percent: 0 },
  { code: 'zw', label: 'zw (zwolnione)', percent: 0 },
]

export interface TotalsLineInput {
  quantity: number
  unitNetPrice: number
  vatRate: VatRateCode
}

export interface InvoiceTotals {
  lineNet: number[]
  vatSummary: VatSummaryRow[]
  totalNet: number
  totalVat: number
  totalGross: number
}

const QTY_SCALE = 1_000_000n

function toScaled(value: number, decimals: number): bigint {
  const [whole, fraction = ''] = Math.abs(value).toFixed(decimals).split('.')
  const scaled = BigInt(whole + fraction)
  return value < 0 ? -scaled : scaled
}

function divideHalfAwayFromZero(numerator: bigint, denominator: bigint): bigint {
  const negative = numerator < 0n
  const abs = negative ? -numerator : numerator
  const quotient = (abs * 2n + denominator) / (denominator * 2n)
  return negative ? -quotient : quotient
}

const toPln = (grosze: bigint): number => Number(grosze) / 100

export function computeTotals(lines: readonly TotalsLineInput[]): InvoiceTotals {
  const netByRate = new Map<VatRateCode, bigint>()
  const lineNet = lines.map((line) => {
    const net = divideHalfAwayFromZero(toScaled(line.quantity, 6) * toScaled(line.unitNetPrice, 2), QTY_SCALE)
    netByRate.set(line.vatRate, (netByRate.get(line.vatRate) ?? 0n) + net)
    return net
  })

  let totalNet = 0n
  let totalVat = 0n
  const vatSummary = VAT_RATES.filter((rate) => netByRate.has(rate.code)).map((rate) => {
    const net = netByRate.get(rate.code) ?? 0n
    const vat = divideHalfAwayFromZero(net * BigInt(rate.percent), 100n)
    totalNet += net
    totalVat += vat
    return { vatRate: rate.code, net: toPln(net), vat: toPln(vat) }
  })

  return {
    lineNet: lineNet.map(toPln),
    vatSummary,
    totalNet: toPln(totalNet),
    totalVat: toPln(totalVat),
    totalGross: toPln(totalNet + totalVat),
  }
}

export function vatRateLabel(code: string): string {
  return VAT_RATES.find((rate) => rate.code === code)?.label ?? code
}
