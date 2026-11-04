import type {
  BuyerKind,
  ExemptionBasisKind,
  IssuedInvoiceDraftRequest,
  IssuedInvoiceResponse,
  PaymentMethod,
  ValidationIssue,
  VatRateCode,
} from '@/api/types'
import { foreignRateFor, isEuCountry } from './foreign'
import { addDaysIso, todayIso } from './format'

export interface InvoiceLineFormValue {
  name: string
  unit: string
  quantity: string
  unitNetPrice: string
  vatRate: VatRateCode
}

export interface BuyerFormValue {
  kind: BuyerKind
  nip: string
  name: string
  addressLine1: string
  addressLine2: string
  countryCode: string
  foreignId: string
  noTaxId: boolean
}

export type ExemptionChoice = 'art113' | ExemptionBasisKind

export interface DraftFormValue {
  buyer: BuyerFormValue
  issueDate: string
  saleDate: string
  dueDate: string
  paymentMethod: PaymentMethod
  splitPayment: boolean
  exemptionChoice: ExemptionChoice
  exemptionText: string
  lines: InvoiceLineFormValue[]
  currency: string
  exchangeRate: string
  exchangeRateDate: string | null
}

export const ART_113 = 'art. 113 ust. 1 ustawy o VAT'

export const emptyLine = (vatRate: VatRateCode = '23'): InvoiceLineFormValue => ({
  name: '',
  unit: 'szt.',
  quantity: '1',
  unitNetPrice: '',
  vatRate,
})

export function emptyDraft(today = todayIso()): DraftFormValue {
  return {
    buyer: { kind: 'Company', nip: '', name: '', addressLine1: '', addressLine2: '', countryCode: '', foreignId: '', noTaxId: false },
    issueDate: today,
    saleDate: today,
    dueDate: addDaysIso(today, 14),
    paymentMethod: 'Transfer',
    splitPayment: false,
    exemptionChoice: 'art113',
    exemptionText: '',
    lines: [emptyLine()],
    currency: 'PLN',
    exchangeRate: '',
    exchangeRateDate: null,
  }
}

export function parseDecimal(value: string): number {
  const parsed = Number(value.replace(/\s+/g, '').replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : 0
}

const formatDecimal = (value: number): string => String(value).replace('.', ',')

const orNull = (value: string): string | null => (value.trim() === '' ? null : value.trim())

export const hasExemptLine = (lines: InvoiceLineFormValue[]): boolean => lines.some((line) => line.vatRate === 'zw')

export const rateToInput = (rate: number | null | undefined): string => (rate ? formatDecimal(rate) : '')

export function withBuyer(form: DraftFormValue, buyer: BuyerFormValue): DraftFormValue {
  const wasForeign = form.buyer.kind === 'Foreign'
  if (buyer.kind !== 'Foreign') {
    return wasForeign
      ? { ...form, buyer, currency: 'PLN', exchangeRate: '', exchangeRateDate: null, lines: form.lines.map((line) => ({ ...line, vatRate: '23' as const })) }
      : { ...form, buyer }
  }
  const sameRateGroup = wasForeign && isEuCountry(buyer.countryCode) === isEuCountry(form.buyer.countryCode)
  const vatRate = foreignRateFor(buyer.countryCode)
  return { ...form, buyer, splitPayment: false, lines: sameRateGroup ? form.lines : form.lines.map((line) => ({ ...line, vatRate })) }
}

export function missingTaxIdError(form: DraftFormValue): ValidationIssue | null {
  const { kind, countryCode, foreignId, noTaxId } = form.buyer
  return kind === 'Foreign' && countryCode && !isEuCountry(countryCode) && !foreignId.trim() && !noTaxId
    ? { code: 'INV-UI-TAX-ID', field: 'buyer.foreignId', message: 'Podaj numer podatkowy nabywcy albo zaznacz „Bez numeru podatkowego”' }
    : null
}

export function toDraftRequest(form: DraftFormValue, storedRate?: number | null): IssuedInvoiceDraftRequest {
  const isCompany = form.buyer.kind === 'Company'
  const isForeign = form.buyer.kind === 'Foreign'
  const hasRate = form.currency !== 'PLN' && form.exchangeRate.trim() !== ''
  return {
    buyer: {
      kind: form.buyer.kind,
      nip: isCompany ? orNull(form.buyer.nip) : null,
      name: orNull(form.buyer.name),
      addressLine1: orNull(form.buyer.addressLine1),
      addressLine2: orNull(form.buyer.addressLine2),
      ...(isForeign ? { countryCode: form.buyer.countryCode.trim(), foreignId: form.buyer.noTaxId ? '' : form.buyer.foreignId.trim() } : {}),
    },
    issueDate: orNull(form.issueDate),
    saleDate: orNull(form.saleDate),
    dueDate: orNull(form.dueDate),
    paymentMethod: form.paymentMethod,
    splitPayment: !isForeign && form.splitPayment,
    exemptionBasis: hasExemptLine(form.lines)
      ? form.exemptionChoice === 'art113'
        ? { kind: 'Act', text: ART_113 }
        : { kind: form.exemptionChoice, text: orNull(form.exemptionText) }
      : null,
    lines: form.lines.map((line) => ({
      name: orNull(line.name),
      unit: orNull(line.unit),
      quantity: parseDecimal(line.quantity),
      unitNetPrice: parseDecimal(line.unitNetPrice),
      vatRate: line.vatRate,
    })),
    currency: form.currency,
    exchangeRate: hasRate ? parseDecimal(form.exchangeRate) : form.currency !== 'PLN' && storedRate != null ? 0 : null,
    exchangeRateDate: hasRate ? form.exchangeRateDate : null,
  }
}

export function fromResponse(invoice: IssuedInvoiceResponse): DraftFormValue {
  const basis = invoice.exemptionBasis
  return {
    buyer: {
      kind: invoice.buyer.kind,
      nip: invoice.buyer.nip ?? '',
      name: invoice.buyer.name ?? '',
      addressLine1: invoice.buyer.addressLine1 ?? '',
      addressLine2: invoice.buyer.addressLine2 ?? '',
      countryCode: invoice.buyer.countryCode ?? '',
      foreignId: invoice.buyer.foreignId ?? '',
      noTaxId: Boolean(invoice.buyer.countryCode) && !isEuCountry(invoice.buyer.countryCode ?? '') && !invoice.buyer.foreignId,
    },
    issueDate: invoice.issueDate ?? '',
    saleDate: invoice.saleDate ?? '',
    dueDate: invoice.dueDate ?? '',
    paymentMethod: invoice.paymentMethod ?? 'Transfer',
    splitPayment: invoice.splitPayment,
    exemptionChoice: !basis || (basis.kind === 'Act' && basis.text === ART_113) ? 'art113' : basis.kind,
    exemptionText: basis && !(basis.kind === 'Act' && basis.text === ART_113) ? (basis.text ?? '') : '',
    lines: invoice.lines.length
      ? invoice.lines.map((line) => ({
          name: line.name ?? '',
          unit: line.unit ?? '',
          quantity: formatDecimal(line.quantity),
          unitNetPrice: formatDecimal(line.unitNetPrice),
          vatRate: line.vatRate,
        }))
      : [emptyLine()],
    currency: invoice.currency,
    exchangeRate: rateToInput(invoice.exchangeRate),
    exchangeRateDate: invoice.exchangeRate ? (invoice.exchangeRateDate ?? null) : null,
  }
}

export function toFieldErrors(issues: ValidationIssue[]): Record<string, string> {
  const result: Record<string, string> = {}
  for (const issue of issues) {
    if (issue.field && !(issue.field in result)) {
      result[issue.field] = issue.message
    }
  }
  return result
}
