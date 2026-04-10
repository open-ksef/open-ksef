import type { ReactElement } from 'react'

type InvoiceSource = 'Aggregate' | 'Synced'

const sourcePresentation: Record<InvoiceSource, { label: string; tone: 'aggregate' | 'synced' }> = {
  Aggregate: { label: 'Aggregate', tone: 'aggregate' },
  Synced: { label: 'Sync', tone: 'synced' },
}

interface SourceChipProps {
  source: InvoiceSource
}

export function SourceChip({ source }: SourceChipProps): ReactElement {
  const presentation = sourcePresentation[source]

  return (
    <span className={`invoice-source-chip invoice-source-chip--${presentation.tone}`} aria-label={presentation.label}>
      {presentation.label}
    </span>
  )
}
