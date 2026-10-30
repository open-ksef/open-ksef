import type { VatRateCode } from '@/api/types'

export interface InvoiceLineFormValue {
  name: string
  unit: string
  quantity: string
  unitNetPrice: string
  vatRate: VatRateCode
}

export const emptyLine = (): InvoiceLineFormValue => ({
  name: '',
  unit: 'szt.',
  quantity: '1',
  unitNetPrice: '',
  vatRate: '23',
})

export function parseDecimal(value: string): number {
  const parsed = Number(value.replace(/\s+/g, '').replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : 0
}

export function formatDecimal(value: number): string {
  return String(value).replace('.', ',')
}
