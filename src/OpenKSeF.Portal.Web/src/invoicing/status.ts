import type { IssuedInvoiceResponse, IssuedInvoiceStatus } from '@/api/types'

const FAST_POLL_WINDOW_MS = 120_000

export function ksefPollInterval(status: IssuedInvoiceStatus | undefined, elapsedMs: number): number | false {
  if (status !== 'Sending') return false
  return elapsedMs < FAST_POLL_WINDOW_MS ? 3_000 : 30_000
}

export const isReissueBlocked = ({ status, ksef }: IssuedInvoiceResponse): boolean =>
  status === 'Rejected' && ksef.statusCode === 440

// Stored by IssuedInvoiceSendProcessor. The API has no cause field, and only a new token or certificate fixes these. Pre-T-652 texts stay for older rows.
export const TOKEN_REJECTIONS = [
  'Brak uprawnienia do wystawiania faktur w KSeF. Podmień w ustawieniach firmy token lub certyfikat na taki, który ma uprawnienie do zapisu.',
  'KSeF odrzucił token (nieprawidłowy lub unieważniony). Wygeneruj nowy token i podmień go w ustawieniach firmy.',
  'Wysyłka faktur wymaga tokenu lub certyfikatu KSeF w ustawieniach firmy',
  'KSeF odrzucił certyfikat (nieprawidłowy, wygasły lub unieważniony). Wgraj nowy certyfikat KSeF w ustawieniach firmy.',
  'Token KSeF nie ma uprawnienia do wystawiania faktur. Wygeneruj token z uprawnieniem do zapisu i podmień go w ustawieniach firmy.',
  'Wysyłka faktur wymaga tokenu KSeF w ustawieniach firmy',
]

export type RejectionCause = 'token' | 'duplicate' | 'data'

export function rejectionCause(invoice: IssuedInvoiceResponse): RejectionCause {
  if (isReissueBlocked(invoice)) return 'duplicate'
  return TOKEN_REJECTIONS.includes(invoice.ksef.statusMessage ?? '') ? 'token' : 'data'
}

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
