import type { AdvanceDraftRequest, FinalDraftRequest, IssuedInvoiceResponse, IssuedInvoiceType, VatRateCode, VatSummaryRow } from '@/api/types'
import { parseDecimal, toDraftRequest, type DraftFormValue } from './form'
import { ALL_RATES, computeTotals, divideHalfAwayFromZero, toScaled, type TotalsLineInput } from './totals'

export interface Buckets {
  rows: VatSummaryRow[]
  totalNet: number
  totalVat: number
  totalGross: number
}

const toPln = (grosze: bigint): number => Number(grosze) / 100
const grosze = (value: number): bigint => toScaled(value, 2)
const percentOf = (code: string): number => ALL_RATES.find((rate) => rate.code === code)?.percent ?? 0

function buckets(rows: VatSummaryRow[]): Buckets {
  const totalNet = rows.reduce((sum, row) => sum + grosze(row.net), 0n)
  const totalVat = rows.reduce((sum, row) => sum + grosze(row.vat), 0n)
  return { rows, totalNet: toPln(totalNet), totalVat: toPln(totalVat), totalGross: toPln(totalNet + totalVat) }
}

function grossByRate(order: readonly TotalsLineInput[]): Map<VatRateCode, bigint> {
  const result = new Map<VatRateCode, bigint>()
  for (const rate of ALL_RATES) {
    for (const line of order.filter((item) => item.vatRate === rate.code)) {
      const net = divideHalfAwayFromZero(toScaled(line.quantity, 6) * toScaled(line.unitNetPrice, 2), 1_000_000n)
      result.set(rate.code, (result.get(rate.code) ?? 0n) + net + divideHalfAwayFromZero(net * BigInt(rate.percent), 100n))
    }
  }
  return result
}

export const orderValue = (order: readonly TotalsLineInput[]): number => toPln([...grossByRate(order).values()].reduce((sum, value) => sum + value, 0n))

export function splitAdvance(order: readonly TotalsLineInput[], amount: number): Buckets {
  const byRate = [...grossByRate(order)]
  const total = byRate.reduce((sum, [, value]) => sum + value, 0n)
  if (total === 0n) return buckets(byRate.map(([vatRate]) => ({ vatRate, net: 0, vat: 0 })))
  const received = grosze(amount)
  const shares = byRate.map(([, value]) => divideHalfAwayFromZero(received * value, total))
  const largest = byRate.reduce((best, [, value], index) => (value > byRate[best][1] ? index : best), 0)
  shares[largest] += received - shares.reduce((sum, value) => sum + value, 0n)
  return buckets(
    byRate.map(([vatRate], index) => {
      const percent = BigInt(percentOf(vatRate))
      const vat = divideHalfAwayFromZero(shares[index] * percent, 100n + percent)
      return { vatRate, net: toPln(shares[index] - vat), vat: toPln(vat) }
    }),
  )
}

export function remainingAfterAdvances(lines: readonly TotalsLineInput[], advances: readonly VatSummaryRow[]): Buckets {
  const full = computeTotals(lines).vatSummary
  return buckets(
    full.map((row) => {
      const paid = advances.filter((advance) => advance.vatRate === row.vatRate)
      return {
        vatRate: row.vatRate,
        net: toPln(grosze(row.net) - paid.reduce((sum, advance) => sum + grosze(advance.net), 0n)),
        vat: toPln(grosze(row.vat) - paid.reduce((sum, advance) => sum + grosze(advance.vat), 0n)),
      }
    }),
  )
}

export const exceedsAdvances = (remaining: Buckets): boolean => remaining.totalGross < 0 || remaining.rows.some((row) => row.net < 0 || row.vat < 0)

export const KIND_LABELS: Record<IssuedInvoiceType, string> = {
  VAT: 'Faktura',
  KOR: 'Faktura korygująca',
  ZAL: 'Faktura zaliczkowa',
  ROZ: 'Faktura rozliczająca',
  KOR_ZAL: 'Korekta faktury zaliczkowej',
  KOR_ROZ: 'Korekta faktury rozliczającej',
}

export const LIST_KIND_LABELS: Partial<Record<IssuedInvoiceType, string>> = { KOR: 'Korekta', ZAL: 'Zaliczka', ROZ: 'Rozliczenie', KOR_ZAL: 'Korekta', KOR_ROZ: 'Korekta' }

export const isChainDraft = (invoice: { invoiceType?: IssuedInvoiceType }): boolean => invoice.invoiceType === 'ZAL' || invoice.invoiceType === 'ROZ'

export const isFirstAdvance = (invoice: IssuedInvoiceResponse): boolean => invoice.invoiceType === 'ZAL' && (!invoice.orderChainId || invoice.orderChainId === invoice.id)

export const formLines = (form: DraftFormValue): TotalsLineInput[] =>
  form.lines.map((line) => ({ quantity: parseDecimal(line.quantity), unitNetPrice: parseDecimal(line.unitNetPrice), vatRate: line.vatRate }))

export function paidBefore(invoice: IssuedInvoiceResponse): number {
  const chain = invoice.chain
  if (!chain) return 0
  const accepted = [...chain.advances, ...chain.corrections.filter((item) => item.invoiceType === 'KOR_ZAL')].filter(
    (item) => item.id !== invoice.id && item.status === 'Accepted',
  )
  return toPln(accepted.reduce((sum, item) => sum + grosze(item.totalGross), 0n))
}

export function advancesOf(invoice: IssuedInvoiceResponse): VatSummaryRow[] {
  const full = computeTotals(invoice.lines).vatSummary
  return full.map((row) => {
    const own = invoice.vatSummary.find((item) => item.vatRate === row.vatRate)
    return { vatRate: row.vatRate, net: toPln(grosze(row.net) - grosze(own?.net ?? 0)), vat: toPln(grosze(row.vat) - grosze(own?.vat ?? 0)) }
  })
}

export function toAdvanceRequest(form: DraftFormValue, advanceAmount: string, first: boolean, storedRate?: number | null): AdvanceDraftRequest {
  const draft = toDraftRequest(form, storedRate)
  return {
    buyer: first ? draft.buyer : null,
    issueDate: draft.issueDate,
    paymentDate: draft.saleDate,
    advanceAmount: advanceAmount.trim() ? parseDecimal(advanceAmount) : null,
    paymentMethod: draft.paymentMethod,
    currency: first ? draft.currency : null,
    exchangeRate: draft.exchangeRate,
    exchangeRateDate: draft.exchangeRateDate,
    exemptionBasis: draft.exemptionBasis,
    lines: draft.lines,
  }
}

export function toFinalRequest(form: DraftFormValue, storedRate?: number | null): FinalDraftRequest {
  const draft = toDraftRequest(form, storedRate)
  return {
    issueDate: draft.issueDate,
    saleDate: draft.saleDate,
    dueDate: draft.dueDate,
    paymentMethod: draft.paymentMethod,
    splitPayment: draft.splitPayment,
    exchangeRate: draft.exchangeRate,
    exchangeRateDate: draft.exchangeRateDate,
    exemptionBasis: draft.exemptionBasis,
    lines: draft.lines,
  }
}

export function advanceDelta(state: readonly TotalsLineInput[], after: readonly TotalsLineInput[], currentAmount: number, afterAmount: number): Buckets {
  const before = splitAdvance(state, currentAmount).rows
  const next = splitAdvance(
    after.filter((line) => line.quantity !== 0),
    afterAmount,
  ).rows
  const pick = (rows: VatSummaryRow[], code: string): VatSummaryRow | undefined => rows.find((row) => row.vatRate === code)
  return buckets(
    ALL_RATES.filter((rate) => pick(before, rate.code) || pick(next, rate.code)).map(({ code }) => ({
      vatRate: code,
      net: toPln(grosze(pick(next, code)?.net ?? 0) - grosze(pick(before, code)?.net ?? 0)),
      vat: toPln(grosze(pick(next, code)?.vat ?? 0) - grosze(pick(before, code)?.vat ?? 0)),
    })),
  )
}
