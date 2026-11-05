export function generateInvoice(
  file: File,
  additionalData: { nrKSeF: string; qrCode?: string },
  formatType: 'blob',
): Promise<Blob>
