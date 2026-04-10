import type { ReactElement } from 'react'

interface DocumentNumberPreviewProps {
  policyResolved: string
  externalReference?: string | null
}

export function DocumentNumberPreview({
  policyResolved,
  externalReference = null,
}: DocumentNumberPreviewProps): ReactElement {
  return (
    <section className="document-number-preview" aria-label="Podgląd numeru dokumentu">
      <h2 className="document-number-preview__title">Numer dokumentu</h2>
      <p className="document-number-preview__value">{policyResolved}</p>
      {externalReference ? (
        <p className="document-number-preview__reference">Referencja zewnętrzna: {externalReference}</p>
      ) : null}
    </section>
  )
}
