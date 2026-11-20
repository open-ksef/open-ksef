import type {
  CorrectionDraftRequest,
  CorrectionType,
  IssuedInvoiceBuyer,
  IssuedInvoiceResponse,
  IssuedInvoiceType,
  PaymentMethod,
  VatRateCode,
  VatSummaryRow,
} from '@/api/types'
import { parseDecimal, type InvoiceLineFormValue } from './form'
import { ALL_RATES, computeTotals } from './totals'

export interface CorrectionStateLine {
  lineNo: number
  name: string
  unit: string
  quantity: number
  unitNetPrice: number
  vatRate: VatRateCode
}

export interface CorrectionAfterLine {
  baseLineNo?: number | null
  name: string
  unit: string
  quantity: number
  unitNetPrice: number
  vatRate: VatRateCode
}

export interface CorrectionRowView {
  rowNo: number
  isBefore: boolean
  baseLineNo: number | null
  name: string
  unit: string
  quantity: number
  unitNetPrice: number
  vatRate: VatRateCode
  netAmount: number
}

export interface CorrectionResult {
  rows: CorrectionRowView[]
  deltas: VatSummaryRow[]
  totalNet: number
  totalVat: number
  totalGross: number
}

const cents = (value: number): number => Math.round(value * 100) / 100

const sameLine = (before: CorrectionStateLine, after: CorrectionAfterLine): boolean =>
  before.name.trim() === after.name.trim() &&
  before.unit.trim() === after.unit.trim() &&
  before.quantity === after.quantity &&
  before.unitNetPrice === after.unitNetPrice &&
  before.vatRate === after.vatRate

export function computeCorrection(state: readonly CorrectionStateLine[], after: readonly CorrectionAfterLine[]): CorrectionResult {
  const emitted: Omit<CorrectionRowView, 'rowNo' | 'netAmount'>[] = []
  const emit = (line: CorrectionAfterLine, isBefore: boolean, baseLineNo: number | null): void => {
    emitted.push({ isBefore, baseLineNo, name: line.name, unit: line.unit, quantity: line.quantity, unitNetPrice: line.unitNetPrice, vatRate: line.vatRate })
  }
  for (const before of state) {
    const changed = after.find((line) => line.baseLineNo === before.lineNo) ?? { ...before, quantity: 0 }
    if (changed.quantity !== 0 && sameLine(before, changed)) continue
    emit(before, true, before.lineNo)
    emit(changed, false, before.lineNo)
  }
  for (const added of after) {
    if (added.baseLineNo == null && added.quantity !== 0) emit(added, false, null)
  }

  const { lineNet } = computeTotals(emitted)
  const rows = emitted.map((row, index) => ({ ...row, rowNo: index + 1, netAmount: lineNet[index] }))

  const beforeTotals = computeTotals(state)
  const afterTotals = computeTotals(after.filter((line) => line.quantity !== 0))
  const bucket = (summary: VatSummaryRow[], rate: VatRateCode): VatSummaryRow | undefined => summary.find((row) => row.vatRate === rate)
  const touched = new Set(rows.map((row) => row.vatRate))
  const deltas = ALL_RATES.filter((rate) => touched.has(rate.code)).map(({ code }) => ({
    vatRate: code,
    net: cents((bucket(afterTotals.vatSummary, code)?.net ?? 0) - (bucket(beforeTotals.vatSummary, code)?.net ?? 0)),
    vat: cents((bucket(afterTotals.vatSummary, code)?.vat ?? 0) - (bucket(beforeTotals.vatSummary, code)?.vat ?? 0)),
  }))
  const totalNet = cents(deltas.reduce((sum, row) => sum + row.net, 0))
  const totalVat = cents(deltas.reduce((sum, row) => sum + row.vat, 0))
  return { rows, deltas, totalNet, totalVat, totalGross: cents(totalNet + totalVat) }
}

export const CORRECTION_REASONS = [
  'Rabat',
  'Zwrot towaru',
  'Błąd w cenie lub ilości',
  'Błąd w stawce VAT',
  'Błąd w danych nabywcy',
  'Błędny NIP nabywcy',
] as const

export const CORRECTION_TYPES: readonly { value: CorrectionType; label: string }[] = [
  { value: 1, label: 'Skutek w dacie ujęcia faktury pierwotnej' },
  { value: 2, label: 'Skutek w dacie wystawienia faktury korygującej' },
  { value: 3, label: 'Skutek w innej dacie, w tym gdy daty dla pozycji są różne' },
]

export const CORRECTION_KINDS: readonly IssuedInvoiceType[] = ['KOR', 'KOR_ZAL', 'KOR_ROZ']

export const isCorrection = (invoice: { invoiceType?: IssuedInvoiceType }): boolean => CORRECTION_KINDS.includes(invoice.invoiceType ?? 'VAT')

export interface CorrectionLineFormValue extends InvoiceLineFormValue {
  baseLineNo?: number | null
}

export interface CorrectionFormValue {
  issueDate: string
  dueDate: string
  paymentMethod: PaymentMethod
  correctionReason: string
  correctionType: '' | `${CorrectionType}`
  buyerName: string
  buyerAddressLine1: string
  buyerAddressLine2: string
  buyerCountryCode: string
  lines: CorrectionLineFormValue[]
}

const toInput = (value: number): string => String(value).replace('.', ',')

export const canCorrectCountry = (buyer: IssuedInvoiceBuyer): boolean => buyer.kind === 'Foreign' && !buyer.foreignId

export function stateOf(original: IssuedInvoiceResponse): { lines: CorrectionStateLine[]; buyer: IssuedInvoiceBuyer } {
  return { lines: original.currentState?.lines ?? original.lines, buyer: original.currentState?.buyer ?? original.buyer }
}

export function fromCorrection(invoice: IssuedInvoiceResponse): CorrectionFormValue {
  return {
    issueDate: invoice.issueDate ?? '',
    dueDate: invoice.dueDate ?? '',
    paymentMethod: invoice.paymentMethod ?? 'Transfer',
    correctionReason: invoice.correctionReason ?? '',
    correctionType: invoice.correctionType ? `${invoice.correctionType}` : '',
    buyerName: invoice.buyer.name ?? '',
    buyerAddressLine1: invoice.buyer.addressLine1 ?? '',
    buyerAddressLine2: invoice.buyer.addressLine2 ?? '',
    buyerCountryCode: invoice.buyer.countryCode ?? '',
    lines: invoice.lines.map((line) => ({
      baseLineNo: line.baseLineNo ?? null,
      name: line.name,
      unit: line.unit,
      quantity: toInput(line.quantity),
      unitNetPrice: toInput(line.unitNetPrice),
      vatRate: line.vatRate,
    })),
  }
}

export const afterLines = (form: CorrectionFormValue): CorrectionAfterLine[] =>
  form.lines.map((line) => ({
    baseLineNo: line.baseLineNo ?? null,
    name: line.name,
    unit: line.unit,
    quantity: parseDecimal(line.quantity),
    unitNetPrice: parseDecimal(line.unitNetPrice),
    vatRate: line.vatRate,
  }))

export function toCorrectionRequest(form: CorrectionFormValue, buyer: IssuedInvoiceBuyer): CorrectionDraftRequest {
  const text = (value: string): string | null => value.trim() || null
  return {
    issueDate: text(form.issueDate),
    dueDate: text(form.dueDate),
    paymentMethod: form.paymentMethod,
    correctionReason: form.correctionReason.trim(),
    correctionType: form.correctionType ? (Number(form.correctionType) as CorrectionType) : null,
    buyer: {
      ...buyer,
      name: text(form.buyerName),
      addressLine1: text(form.buyerAddressLine1),
      addressLine2: text(form.buyerAddressLine2),
      ...(canCorrectCountry(buyer) ? { countryCode: text(form.buyerCountryCode) } : {}),
    },
    lines: afterLines(form).map((line) => ({ ...line, name: text(line.name), unit: text(line.unit) })),
  }
}
