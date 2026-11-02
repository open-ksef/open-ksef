import type { IssuedInvoiceResponse, IssuedInvoiceStatus } from '@/api/types'

const FAST_POLL_WINDOW_MS = 120_000

export function ksefPollInterval(status: IssuedInvoiceStatus | undefined, elapsedMs: number): number | false {
  if (status !== 'Sending') return false
  return elapsedMs < FAST_POLL_WINDOW_MS ? 3_000 : 30_000
}

export const isReissueBlocked = ({ status, ksef }: IssuedInvoiceResponse): boolean =>
  status === 'Rejected' && ksef.statusCode === 440

export const isUpoPending = ({ status, ksef }: IssuedInvoiceResponse): boolean => status === 'Accepted' && !ksef.hasUpo

export function downloadFile(content: BlobPart, fileName: string, type: string): void {
  const url = URL.createObjectURL(new Blob([content], { type }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}

export const fileStem = (number: string | null, id: string): string => (number ?? id).replace(/[^\w.-]+/g, '_')

export async function renderInvoicePdf(xml: string, nrKSeF: string, qrCode: string): Promise<Blob> {
  const { generateInvoice } = await import('@/vendor/ksef-pdf-generator/ksef-fe-invoice-converter.js')
  return generateInvoice(new File([xml], 'faktura.xml', { type: 'application/xml' }), { nrKSeF, qrCode }, 'blob')
}
