import type {
  BuyerKind,
  ExemptionBasisKind,
  IssuedInvoiceDraftRequest,
  IssuedInvoiceResponse,
  PaymentMethod,
  ValidationIssue,
  VatRateCode,
} from '@/api/types'
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
}

export const ART_113 = 'art. 113 ust. 1 ustawy o VAT'

export const emptyLine = (): InvoiceLineFormValue => ({
  name: '',
  unit: 'szt.',
  quantity: '1',
  unitNetPrice: '',
  vatRate: '23',
})

export function emptyDraft(today = todayIso()): DraftFormValue {
  return {
    buyer: { kind: 'Company', nip: '', name: '', addressLine1: '', addressLine2: '' },
    issueDate: today,
    saleDate: today,
    dueDate: addDaysIso(today, 14),
    paymentMethod: 'Transfer',
    splitPayment: false,
    exemptionChoice: 'art113',
    exemptionText: '',
    lines: [emptyLine()],
  }
}

export function parseDecimal(value: string): number {
  const parsed = Number(value.replace(/\s+/g, '').replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : 0
}

const formatDecimal = (value: number): string => String(value).replace('.', ',')

const orNull = (value: string): string | null => (value.trim() === '' ? null : value.trim())

export const hasExemptLine = (lines: InvoiceLineFormValue[]): boolean => lines.some((line) => line.vatRate === 'zw')

export function toDraftRequest(form: DraftFormValue): IssuedInvoiceDraftRequest {
  const isCompany = form.buyer.kind === 'Company'
  return {
    buyer: {
      kind: form.buyer.kind,
      nip: isCompany ? orNull(form.buyer.nip) : null,
      name: orNull(form.buyer.name),
      addressLine1: orNull(form.buyer.addressLine1),
      addressLine2: orNull(form.buyer.addressLine2),
    },
    issueDate: orNull(form.issueDate),
    saleDate: orNull(form.saleDate),
    dueDate: orNull(form.dueDate),
    paymentMethod: form.paymentMethod,
    splitPayment: form.splitPayment,
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
