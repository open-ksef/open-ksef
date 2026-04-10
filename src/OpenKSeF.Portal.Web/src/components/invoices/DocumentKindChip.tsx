import type { ReactElement } from 'react'

import type { DocumentKind } from '@/api/schemas/invoice'

const kindPresentation: Record<
  DocumentKind,
  { label: string; tone: 'vat' | 'advance' | 'final' | 'proforma' | 'correction' }
> = {
  VatInvoice: { label: 'Faktura VAT', tone: 'vat' },
  AdvanceInvoice: { label: 'Faktura zaliczkowa', tone: 'advance' },
  FinalInvoice: { label: 'Faktura finalna', tone: 'final' },
  Proforma: { label: 'Pro forma', tone: 'proforma' },
  CorrectionInvoice: { label: 'Faktura korygująca', tone: 'correction' },
}

interface DocumentKindChipProps {
  kind: DocumentKind
}

export function DocumentKindChip({ kind }: DocumentKindChipProps): ReactElement {
  const presentation = kindPresentation[kind]

  return (
    <span className={`invoice-kind-chip invoice-kind-chip--${presentation.tone}`} aria-label={presentation.label}>
      {presentation.label}
    </span>
  )
}
