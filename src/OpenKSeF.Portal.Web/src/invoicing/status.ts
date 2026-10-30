import type { IssuedInvoiceStatus } from '@/api/types'

const FAST_POLL_WINDOW_MS = 120_000

export function ksefPollInterval(status: IssuedInvoiceStatus | undefined, elapsedMs: number): number | false {
  if (status !== 'Sending') return false
  return elapsedMs < FAST_POLL_WINDOW_MS ? 3_000 : 30_000
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
